using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static MsgHatch.Data.MongoDb.Tests.Integration.TestTypes.SignalWatch;

namespace MsgHatch.Data.MongoDb.Tests.Integration;

/// <summary>
/// The MongoDB change stream signal (D303): the use cases of each signal, and the wake of a wait by an insert of a store
/// on the same database. Each test has its own database, so no insert of another test reaches its watch.
/// </summary>
public sealed class MongoDbDispatchSignalIntegTests : DispatchSignalTestsBase<MongoDbDispatchSignal>, IDisposable
{
    private const int _watchBroken = 2800;
    private const int _watchWorksAgain = 2801;

    private static readonly DispatchTopic _topic = DispatchTopic.ForChannel(TestChannel.Id);

    private readonly TestDatabase _database;
    private readonly WaitRecordingTimeProvider _time;
    private readonly FakeLogger<MongoDbDispatchSignal> _logger;

    public MongoDbDispatchSignalIntegTests(MongoDbFixture server)
        : this(server.Database($"signal{Guid.NewGuid():N}"[..18]), new WaitRecordingTimeProvider(), new FakeLogger<MongoDbDispatchSignal>())
    {
    }

    private MongoDbDispatchSignalIntegTests(TestDatabase database, WaitRecordingTimeProvider time, FakeLogger<MongoDbDispatchSignal> logger)
        : base(new MongoDbDispatchSignal(database.Database, time, logger)) =>
        (_database, _time, _logger) = (database, time, logger);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Signal.Dispose();

    [Fact]
    public async Task WaitAsync_ReceivedMessageThatTheInboxStoreOfAnotherHostAdds_CompletesTheInboxWait()
    {
        // Arrange
        await OpenTheWatchAsync();
        var store = new MongoDbInboxStore(_database.Database, new MsgHatchJson(TestContentTypes.ARegistry())).For(TenantKey.None);
        var wait = Signal.WaitAsync(DispatchTopic.Inbox, Ct);

        // Act
        await store.AddAsync(TestMessages.AReceivedMessage(new TestEvent("text")), Ct);

        // Assert
        Assert.Null(await CompletedAsync(wait));
    }

    [Fact]
    public async Task WaitAsync_OutgoingMessageThatTheOutboxStoreOfAnotherHostAdds_CompletesOnlyTheWaitOfItsChannel()
    {
        // Arrange
        await OpenTheWatchAsync();
        var store = new MongoDbOutboxStore(_database.Database, new MsgHatchJson(TestContentTypes.ARegistry())).For(TenantKey.None);
        var wait = Signal.WaitAsync(_topic, Ct);
        var otherChannel = Signal.WaitAsync(DispatchTopic.ForChannel(TestChannel.SecondId), Ct).AsTask();
        var inbox = Signal.WaitAsync(DispatchTopic.Inbox, Ct).AsTask();

        // Act
        await store.AddAsync([TestMessages.AnOutgoingMessage(channel: TestChannel.Id)], Ct);

        // Assert
        Assert.Null(await CompletedAsync(wait));
        await WaitUntilTheWatchHandledTheChangesBeforeAsync();
        Assert.Equal((false, false), (otherChannel.IsCompleted, inbox.IsCompleted));
    }

    [Fact]
    public async Task WaitAsync_UpdateOfAMessageOrInsertIntoAnotherCollection_DoesNotCompleteTheWait()
    {
        // Arrange
        await OpenTheWatchAsync();
        await Collection(MongoDbOutboxStore.CollectionName).InsertOneAsync(AnOutboxDocument(TestChannel.Id.Name), cancellationToken: Ct);
        await Collection(MongoDbInboxStore.CollectionName).InsertOneAsync(new BsonDocument("text", "first"), cancellationToken: Ct);
        Assert.Null(await CompletedAsync(Signal.WaitAsync(_topic, Ct)));
        Assert.Null(await CompletedAsync(Signal.WaitAsync(DispatchTopic.Inbox, Ct)));
        var wait = Signal.WaitAsync(_topic, Ct).AsTask();
        var inbox = Signal.WaitAsync(DispatchTopic.Inbox, Ct).AsTask();

        // Act
        await Collection(MongoDbOutboxStore.CollectionName).UpdateManyAsync(
            FilterDefinition<BsonDocument>.Empty, new BsonDocument("$set", new BsonDocument("note", "changed")), cancellationToken: Ct);
        await Collection(MongoDbInboxStore.CollectionName).UpdateManyAsync(
            FilterDefinition<BsonDocument>.Empty, new BsonDocument("$set", new BsonDocument("note", "changed")), cancellationToken: Ct);
        await Collection(MongoDbOutboxStore.ChannelStateCollectionName).InsertOneAsync(AnOutboxDocument(TestChannel.Id.Name), cancellationToken: Ct);
        await Collection("Other").InsertOneAsync(AnOutboxDocument(TestChannel.Id.Name), cancellationToken: Ct);

        // Assert
        await WaitUntilTheWatchHandledTheChangesBeforeAsync();
        Assert.Equal((false, false), (wait.IsCompleted, inbox.IsCompleted));
    }

    [Fact]
    public async Task WaitAsync_WatchEndsWithTheDropOfTheDatabase_ReturnsUnavailableForTheWaitingLoopAndAtOnceForTheNextWait()
    {
        // Arrange
        await OpenTheWatchAsync();
        var waiting = Signal.WaitAsync(_topic, Ct);

        // Act
        await _database.Database.Client.DropDatabaseAsync(_database.Database.DatabaseNamespace.DatabaseName, Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, (await CompletedAsync(waiting))?.Type);
        var next = Signal.WaitAsync(_topic, Ct);
        Assert.True(next.IsCompleted);
        Assert.Equal(Errors.Unavailable, (await next)?.Type);
        Assert.Equal([(_watchBroken, LogLevel.Warning)], [.. _logger.Collector.GetSnapshot().Select(static record => (record.Id.Id, record.Level))]);
    }

    [Fact]
    public async Task WaitAsync_PauseAfterTheWatchEnds_OpensTheWatchAgainAndLogsOneInformation()
    {
        // Arrange
        await OpenTheWatchAsync();
        var waiting = Signal.WaitAsync(_topic, Ct);
        await _database.Database.Client.DropDatabaseAsync(_database.Database.DatabaseNamespace.DatabaseName, Ct);
        await CompletedAsync(waiting);
        var pause = await _time.NextWaitAsync();

        // Act
        _time.Advance(pause);

        // Assert
        await OpenTheWatchAsync();
        var wait = Signal.WaitAsync(_topic, Ct);
        await Collection(MongoDbOutboxStore.CollectionName).InsertOneAsync(AnOutboxDocument(TestChannel.Id.Name), cancellationToken: Ct);
        Assert.Null(await CompletedAsync(wait));
        Assert.Equal(
            [(_watchBroken, LogLevel.Warning), (_watchWorksAgain, LogLevel.Information)],
            [.. _logger.Collector.GetSnapshot().Select(static record => (record.Id.Id, record.Level))]);
    }

    [Fact]
    public async Task WaitAsync_ServerThatTheDriverCannotReach_ReturnsUnavailableAndLogsOneWarningForTwoFailedOpens()
    {
        // Arrange
        var logger = new FakeLogger<MongoDbDispatchSignal>();
        using var signal = new MongoDbDispatchSignal(UnreachableServer.Database, _time, logger);

        // Act
        var error = await CompletedAsync(signal.WaitAsync(_topic, Ct));

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
        Assert.Equal(Errors.Unavailable, (await signal.WaitAsync(_topic, Ct))?.Type);
        _time.Advance(await _time.NextWaitAsync());
        await _time.NextWaitAsync();
        var warning = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal((_watchBroken, LogLevel.Warning, true), (warning.Id.Id, warning.Level, warning.Exception is TimeoutException));
    }

    [Fact]
    public async Task WaitAsync_NotificationOfAnotherSignalOnTheDatabase_CompletesTheWait()
    {
        // Arrange
        using var other = new MongoDbDispatchSignal(_database.Database, _time, new FakeLogger<MongoDbDispatchSignal>());
        await OpenTheWatchAsync();
        var wait = Signal.WaitAsync(_topic, Ct);

        // Act
        await other.NotifyAsync(_topic, Ct);

        // Assert
        Assert.Null(await CompletedAsync(wait));
    }

    [Fact]
    public async Task WaitAsync_OwnNotificationInTheSignalCollection_DoesNotCompleteASecondWait()
    {
        // Arrange
        await OpenTheWatchAsync();
        await Signal.NotifyAsync(_topic, Ct);
        Assert.Null(await CompletedAsync(Signal.WaitAsync(_topic, Ct)));
        await StoredSignalsAsync(count: 1);
        var second = Signal.WaitAsync(_topic, Ct).AsTask();

        // Act
        await WaitUntilTheWatchHandledTheChangesBeforeAsync();

        // Assert
        Assert.False(second.IsCompleted);
    }

    [Fact]
    public async Task NotifyAsync_FirstNotification_InsertsTheTopicWithTheTimeIntoACollectionWithATtlIndexOfOneMinute()
    {
        // Act
        await Signal.NotifyAsync(_topic, Ct);

        // Assert
        var signal = Assert.Single(await StoredSignalsAsync(count: 1));
        Assert.Equal(_topic.Name, signal["topic"].AsString);
        Assert.Equal(TestHost.Start.UtcDateTime, signal["createdAtUtc"].ToUniversalTime());
        var indexes = await (await Collection(MongoDbDispatchSignal.SignalCollectionName).Indexes.ListAsync(Ct)).ToListAsync(Ct);
        Assert.Contains(
            indexes,
            static index => index["key"] == new BsonDocument("createdAtUtc", 1) && index.TryGetValue("expireAfterSeconds", out var ttl) && ttl.ToInt32() == 60);
    }

    [Fact]
    public async Task NotifyAsync_AfterAnInsertOfANotificationThatFailed_ReturnsUnavailable()
    {
        // Arrange
        using var signal = new MongoDbDispatchSignal(UnreachableServer.Database, _time, new FakeLogger<MongoDbDispatchSignal>());
        using var guard = new CancellationTokenSource(TestHost.HangGuard, TimeProvider.System);

        // Act
        var first = await signal.NotifyAsync(_topic, Ct);
        Error? error;
        while ((error = await signal.NotifyAsync(_topic, Ct)) is null)
        {
            await Task.Delay(OneTry, TimeProvider.System, guard.Token);
        }

        // Assert
        Assert.Equal((null, Errors.Unavailable), (first?.Type, error.Type));
    }

    private IMongoCollection<BsonDocument> Collection(string name) => _database.Collection(name);

    /// <summary>Waits until the signal collection holds a number of documents, because a signal inserts them in the background.</summary>
    private async Task<List<BsonDocument>> StoredSignalsAsync(int count)
    {
        using var guard = new CancellationTokenSource(TestHost.HangGuard, TimeProvider.System);
        var signals = await _database.DocumentsAsync(MongoDbDispatchSignal.SignalCollectionName, guard.Token);
        while (signals.Count < count)
        {
            await Task.Delay(OneTry, TimeProvider.System, guard.Token);
            signals = await _database.DocumentsAsync(MongoDbDispatchSignal.SignalCollectionName, guard.Token);
        }

        return signals;
    }

    private Task OpenTheWatchAsync() => SignalWatch.OpenAsync(Signal, _database);

    /// <summary>
    /// Waits until the watch handled each change before this call: the watch handles the changes in their order, so the
    /// wake of the warm-up channel by a new insert comes after them.
    /// </summary>
    private async Task WaitUntilTheWatchHandledTheChangesBeforeAsync()
    {
        var wait = Signal.WaitAsync(WarmUp, Ct);
        await SignalWatch.InsertIntoTheWarmUpChannelAsync(_database, Ct);
        Assert.Null(await CompletedAsync(wait));
    }
}
