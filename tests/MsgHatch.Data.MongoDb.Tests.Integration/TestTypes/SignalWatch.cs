using MongoDB.Bson;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;

/// <summary>
/// The waits of the tests of the MongoDB dispatch signal. The watch of a signal opens in the background, so a test first
/// waits until an insert of a warm-up channel wakes the signal. The watch handles the changes in their order, so a later
/// wake of the warm-up channel also shows that the watch handled each change before it.
/// </summary>
public static class SignalWatch
{
    public static readonly DispatchTopic WarmUp = DispatchTopic.ForChannel(new ChannelId("WarmUp"));

    public static readonly TimeSpan OneTry = TimeSpan.FromMilliseconds(200);

    public static async Task<Error?> CompletedAsync(ValueTask<Error?> wait) =>
        await wait.AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System);

    /// <summary>A document of the outbox with a channel and a unique message id, as the unique index of the outbox store needs.</summary>
    public static BsonDocument AnOutboxDocument(string channel) =>
        new() { { "channel", channel }, { "delivery", new BsonDocument("id", Guid.NewGuid().ToString("D")) } };

    /// <summary>
    /// Waits until the watch of a signal is open: it inserts a message of the warm-up channel until the wait of that
    /// channel completes. While the watch is broken, a wait returns at once, so it waits first for a wait that does not.
    /// </summary>
    public static async Task OpenAsync(IDispatchSignal signal, TestDatabase database)
    {
        using var hang = new CancellationTokenSource(TestHost.HangGuard, TimeProvider.System);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken, hang.Token);
        var wait = signal.WaitAsync(WarmUp, guard.Token).AsTask();
        while (wait.IsCompleted)
        {
            await wait;
            await Task.Delay(OneTry, TimeProvider.System, guard.Token);
            wait = signal.WaitAsync(WarmUp, guard.Token).AsTask();
        }

        while (!wait.IsCompleted)
        {
            await InsertIntoTheWarmUpChannelAsync(database, guard.Token);
            await Task.WhenAny(wait, Task.Delay(OneTry, TimeProvider.System, guard.Token));
        }

        Assert.Null(await wait);
    }

    public static Task InsertIntoTheWarmUpChannelAsync(TestDatabase database, CancellationToken ct) =>
        database.Collection(MongoDbOutboxStore.CollectionName).InsertOneAsync(AnOutboxDocument("WarmUp"), cancellationToken: ct);
}
