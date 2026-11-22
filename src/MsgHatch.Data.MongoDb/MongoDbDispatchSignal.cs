using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Data.MongoDb;

/// <summary>
/// The dispatch signal of the hosts that use one database of the MongoDB stores, also on different machines (D303). One
/// change stream on the database sees each insert into the inbox and the outbox, and notifies its topic on a local
/// <see cref="InProcessDispatchSignal"/>: <see cref="DispatchTopic.Inbox"/> for the inbox, and the topic of the channel
/// of an outgoing message. The watch opens at the first <see cref="WaitAsync"/>. While it does not work,
/// <see cref="WaitAsync"/> returns <see cref="Errors.Unavailable"/>, so the loop uses its fallback wait (section 4.9),
/// and the signal opens the watch again after a pause. The signal keeps no resume token, because a missed change costs
/// at most one poll. A change stream needs a replica set or a sharded cluster.
/// <para>
/// <see cref="NotifyAsync"/> notifies the loops of the own process at once, and inserts the topic into the collection
/// <see cref="SignalCollectionName"/> in the background, with a TTL index of one minute (D304). The watch also sees these
/// inserts, but not the own ones. So a retry, a resume, or a channel check wakes the loops of each host. Many
/// notifications before one insert become one document for each topic.
/// </para>
/// </summary>
public sealed class MongoDbDispatchSignal : IDispatchSignal, IDisposable
{
    /// <summary>The name of the collection of the notifications (D304).</summary>
    public const string SignalCollectionName = "msghatch_signals";

    private const string _channel = "channel";
    private const string _topic = "topic";
    private const string _sender = "sender";
    private const string _createdAtUtc = "createdAtUtc";

    private static readonly TimeSpan _pauseBeforeTheNextWatch = TimeSpan.FromSeconds(5);

    private static readonly CreateIndexModel<BsonDocument> _expiry = new(
        new BsonDocument(_createdAtUtc, 1), new CreateIndexOptions { Name = "IX_msghatch_signals_Created", ExpireAfter = TimeSpan.FromMinutes(1) });

    private readonly IMongoDatabase _database;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly InProcessDispatchSignal _local = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly PipelineDefinition<ChangeStreamDocument<BsonDocument>, BsonDocument> _insertsOfOthers;
    private readonly MongoDbStorage _signals;
    private readonly ConcurrentDictionary<DispatchTopic, bool> _topicsToInsert = new();
    private readonly Channel<bool> _insertWaits =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private Error? _lastInsertError;
    private CancellationTokenSource _watchWorks = new();
    private int _watchStarted;
    private bool _broken;

    /// <summary>Makes the signal on the database of the MongoDB stores.</summary>
    /// <param name="database">The database of the stores.</param>
    /// <param name="time">The clock of the pause before the signal opens a watch again, and of the time of a notification.</param>
    /// <param name="logger">The logger of a watch that does not work.</param>
    public MongoDbDispatchSignal(IMongoDatabase database, TimeProvider time, ILogger<MongoDbDispatchSignal> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        (_database, _time, _logger) = (database, time, logger);
        _insertsOfOthers = InsertsOfMessagesAndOfOtherSignals(_id);
        _signals = new MongoDbStorage(database, SignalCollectionName, [_expiry]);
        _ = InsertTheTopicsUntilDisposedAsync();
    }

    /// <summary>
    /// Notifies the loops of the own process at once, and inserts the topic for the other hosts in the background.
    /// </summary>
    /// <param name="topic">The topic.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or <see cref="Errors.Unavailable"/> while the last insert of a notification failed.</returns>
    public async ValueTask<Error?> NotifyAsync(DispatchTopic topic, CancellationToken ct)
    {
        await _local.NotifyAsync(topic, ct);
        _topicsToInsert[topic] = true;
        _insertWaits.Writer.TryWrite(true);
        return Volatile.Read(ref _lastInsertError);
    }

    /// <inheritdoc />
    public async ValueTask<Error?> WaitAsync(DispatchTopic topic, CancellationToken ct)
    {
        StartTheWatchOnce();
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct, Volatile.Read(ref _watchWorks).Token);
        var error = await _local.WaitAsync(topic, wake.Token);
        return error is null || ct.IsCancellationRequested ? error : new Error(Errors.Unavailable);
    }

    /// <summary>Stops the watch.</summary>
    public void Dispose() => _stop.Cancel();

    private static PipelineDefinition<ChangeStreamDocument<BsonDocument>, BsonDocument> InsertsOfMessagesAndOfOtherSignals(string id) =>
        PipelineDefinition<ChangeStreamDocument<BsonDocument>, BsonDocument>.Create(
        [
            new BsonDocument("$match", new BsonDocument
            {
                { "operationType", "insert" },
                {
                    "$or", new BsonArray
                    {
                        new BsonDocument("ns.coll", new BsonDocument("$in", new BsonArray { MongoDbInboxStore.CollectionName, MongoDbOutboxStore.CollectionName })),
                        new BsonDocument { { "ns.coll", SignalCollectionName }, { $"fullDocument.{_sender}", new BsonDocument("$ne", id) } },
                    }
                },
            }),
            new BsonDocument("$project", new BsonDocument { { "ns.coll", 1 }, { $"fullDocument.{_channel}", 1 }, { $"fullDocument.{_topic}", 1 } }),
        ]);

    private static DispatchTopic? TopicOf(BsonDocument change)
    {
        var document = change["fullDocument"].AsBsonDocument;
        return change["ns"]["coll"].AsString switch
        {
            MongoDbInboxStore.CollectionName => DispatchTopic.Inbox,
            MongoDbOutboxStore.CollectionName => TextOf(document, _channel) is { } channel ? DispatchTopic.ForChannel(new ChannelId(channel)) : null,
            _ => TextOf(document, _topic) is { } topic ? new DispatchTopic(topic) : null,
        };
    }

    private static string? TextOf(BsonDocument document, string field) =>
        document.TryGetValue(field, out var value) && value.IsString ? value.AsString : null;

    private async Task InsertTheTopicsUntilDisposedAsync()
    {
        try
        {
            await foreach (var _ in _insertWaits.Reader.ReadAllAsync(_stop.Token))
            {
                BsonDocument[] notifications = [.. TakeTheTopicsToInsert().Select(NotificationOf)];
                if (notifications.Length > 0)
                {
                    Volatile.Write(ref _lastInsertError, await _signals.RunAsync(() => InsertAsync(notifications), _stop.Token));
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private IEnumerable<DispatchTopic> TakeTheTopicsToInsert() =>
        _topicsToInsert.Keys.Where(topic => _topicsToInsert.TryRemove(topic, out _));

    private BsonDocument NotificationOf(DispatchTopic topic) =>
        new() { { _topic, topic.Name }, { _sender, _id }, { _createdAtUtc, _time.GetUtcNow().UtcDateTime } };

    private async ValueTask<Error?> InsertAsync(BsonDocument[] notifications)
    {
        await _signals.Documents.InsertManyAsync(notifications, cancellationToken: _stop.Token);
        return null;
    }

    private void StartTheWatchOnce()
    {
        if (Interlocked.Exchange(ref _watchStarted, 1) == 0)
        {
            _ = WatchUntilDisposedAsync();
        }
    }

    private async Task WatchUntilDisposedAsync()
    {
        try
        {
            while (true)
            {
                Break(await WatchUntilItEndsAsync());
                await Task.Delay(_pauseBeforeTheNextWatch, _time, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    /// <returns>The exception that ended the watch, or null when the server ended it.</returns>
    private async Task<Exception?> WatchUntilItEndsAsync()
    {
        try
        {
            using var changes = await _database.WatchAsync(_insertsOfOthers, cancellationToken: _stop.Token);
            WorksAgain();
            while (await changes.MoveNextAsync(_stop.Token))
            {
                foreach (var change in changes.Current)
                {
                    if (TopicOf(change) is { } topic)
                    {
                        await _local.NotifyAsync(topic, CancellationToken.None);
                    }
                }
            }

            return null;
        }
        catch (Exception exception) when (!_stop.IsCancellationRequested)
        {
            return exception;
        }
    }

    private void Break(Exception? exception)
    {
        if (!_broken)
        {
            _broken = true;
            _logger.WatchBroken(exception);
        }

        Volatile.Read(ref _watchWorks).Cancel();
    }

    private void WorksAgain()
    {
        if (_broken)
        {
            _broken = false;
            Volatile.Write(ref _watchWorks, new CancellationTokenSource());
            _logger.WatchWorksAgain();
        }
    }
}
