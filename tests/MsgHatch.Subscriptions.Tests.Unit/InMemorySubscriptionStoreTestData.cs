using System.Collections.Concurrent;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Tests.Unit;

/// <summary>
/// The test data of the in-memory subscription store. Each test gets new stores, so a reset has nothing to do.
/// </summary>
public sealed class InMemorySubscriptionStoreTestData : ISubscriptionStoreTestData
{
    /// <summary>Gives one new store for each tenant key, as <c>UseInMemorySubscriptionStore</c> gives them.</summary>
    public static Func<TenantKey, InMemorySubscriptionStore> NewStoreOfEachKey()
    {
        var stores = new ConcurrentDictionary<TenantKey, InMemorySubscriptionStore>();
        return key => stores.GetOrAdd(key, static _ => new InMemorySubscriptionStore());
    }

    public ValueTask ResetAsync(CancellationToken ct) => ValueTask.CompletedTask;
}
