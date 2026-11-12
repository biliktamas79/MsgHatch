namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>What the cache backend interface cannot do: look for an entry in the cache itself, and free the cache.</summary>
public interface ISubscriptionCacheBackendTestData : IAsyncDisposable
{
    ValueTask<bool> ContainsAsync(string key, CancellationToken ct);
}
