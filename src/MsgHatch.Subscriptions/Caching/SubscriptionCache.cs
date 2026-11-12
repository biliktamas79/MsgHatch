using MsgHatch.Domain.Failures;

namespace MsgHatch.Subscriptions.Caching;

/// <summary>The invalidation of the subscription cache of one tenant key, through the tags of that key (section 4.15).</summary>
internal sealed class SubscriptionCache(ISubscriptionCacheBackend backend, SubscriptionCacheKeys keys) : ISubscriptionCache
{
    public ValueTask<Error?> InvalidateAsync(SubscriptionOwner owner, CancellationToken ct) => backend.RemoveByTagAsync(keys.OwnerTag(owner), ct);

    public ValueTask<Error?> InvalidateAllAsync(CancellationToken ct) => backend.RemoveByTagAsync(keys.TenantTag, ct);
}
