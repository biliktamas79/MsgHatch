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
/// </summary>
public sealed class MongoDbDispatchSignal : IDispatchSignal, IDisposable
{
    private const string _channel = "channel";

    private static readonly TimeSpan _pauseBeforeTheNextWatch = TimeSpan.FromSeconds(5);

    private static readonly PipelineDefinition<ChangeStreamDocument<BsonDocument>, BsonDocument> _insertsOfMessages =
        PipelineDefinition<ChangeStreamDocument<BsonDocument>, BsonDocument>.Create(
        [
            new BsonDocument("$match", new BsonDocument
            {
                { "operationType", "insert" },
                { "ns.coll", new BsonDocument("$in", new BsonArray { MongoDbInboxStore.CollectionName, MongoDbOutboxStore.CollectionName }) },
            }),
            new BsonDocument("$project", new BsonDocument { { "ns.coll", 1 }, { $"fullDocument.{_channel}", 1 } }),
        ]);

    private readonly IMongoDatabase _database;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly InProcessDispatchSignal _local = new();
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource _watchWorks = new();
    private int _watchStarted;
    private bool _broken;

    /// <summary>Makes the signal on the database of the MongoDB stores.</summary>
    /// <param name="database">The database of the stores.</param>
    /// <param name="time">The clock of the pause before the signal opens a watch again.</param>
    /// <param name="logger">The logger of a watch that does not work.</param>
    public MongoDbDispatchSignal(IMongoDatabase database, TimeProvider time, ILogger<MongoDbDispatchSignal> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        (_database, _time, _logger) = (database, time, logger);
    }

    /// <inheritdoc />
    public ValueTask<Error?> NotifyAsync(DispatchTopic topic, CancellationToken ct) => _local.NotifyAsync(topic, ct);

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

    private static DispatchTopic? TopicOf(BsonDocument change) =>
        change["ns"]["coll"].AsString == MongoDbInboxStore.CollectionName
            ? DispatchTopic.Inbox
            : change["fullDocument"].AsBsonDocument.TryGetValue(_channel, out var channel) && channel.IsString
                ? DispatchTopic.ForChannel(new ChannelId(channel.AsString))
                : null;

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
            using var changes = await _database.WatchAsync(_insertsOfMessages, cancellationToken: _stop.Token);
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
