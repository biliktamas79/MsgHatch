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

namespace MsgHatch.Data.MongoDb.Tests.Integration;

/// <summary>
/// The MongoDB change stream signal (D303): the use cases of each signal, and the wake of a wait by an insert of a store
/// on the same database. Each test has its own database, so no insert of another test reaches its watch. The watch opens
/// in the background, so a test first waits until an insert of a warm-up channel wakes the signal.
/// </summary>
public sealed class MongoDbDispatchSignalIntegTests : DispatchSignalTestsBase<MongoDbDispatchSignal>, IDisposable
{
    private const int _watchBroken = 2800;
    private const int _watchWorksAgain = 2801;

    private static readonly DispatchTopic _topic = DispatchTopic.ForChannel(TestChannel.Id);
    private static readonly DispatchTopic _warmUp = DispatchTopic.ForChannel(new ChannelId("WarmUp"));
    private static readonly TimeSpan _oneTry = TimeSpan.FromMilliseconds(200);

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

    private static async Task<Error?> CompletedAsync(ValueTask<Error?> wait) =>
        await wait.AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System);

    /// <summary>A document of the outbox with a channel and a unique message id, as the unique index of the outbox store needs.</summary>
    private static BsonDocument AnOutboxDocument(string channel) =>
        new() { { "channel", channel }, { "delivery", new BsonDocument("id", Guid.NewGuid().ToString("D")) } };

    private IMongoCollection<BsonDocument> Collection(string name) => _database.Collection(name);

    /// <summary>
    /// Waits until the watch is open: it inserts a message of the warm-up channel until the wait of that channel
    /// completes. While the watch is broken, a wait returns at once, so it waits first for a wait that does not.
    /// </summary>
    private async Task OpenTheWatchAsync()
    {
        using var guard = new CancellationTokenSource(TestHost.HangGuard, TimeProvider.System);
        var wait = Signal.WaitAsync(_warmUp, guard.Token).AsTask();
        while (wait.IsCompleted)
        {
            await wait;
            await Task.Delay(_oneTry, TimeProvider.System, guard.Token);
            wait = Signal.WaitAsync(_warmUp, guard.Token).AsTask();
        }

        while (!wait.IsCompleted)
        {
            await InsertIntoTheWarmUpChannelAsync(guard.Token);
            await Task.WhenAny(wait, Task.Delay(_oneTry, TimeProvider.System, guard.Token));
        }

        Assert.Null(await wait);
    }

    /// <summary>
    /// Waits until the watch handled each change before this call: the watch handles the changes in their order, so the
    /// wake of the warm-up channel by a new insert comes after them.
    /// </summary>
    private async Task WaitUntilTheWatchHandledTheChangesBeforeAsync()
    {
        var wait = Signal.WaitAsync(_warmUp, Ct);
        await InsertIntoTheWarmUpChannelAsync(Ct);
        Assert.Null(await CompletedAsync(wait));
    }

    private Task InsertIntoTheWarmUpChannelAsync(CancellationToken ct) =>
        Collection(MongoDbOutboxStore.CollectionName).InsertOneAsync(AnOutboxDocument("WarmUp"), cancellationToken: ct);
}
