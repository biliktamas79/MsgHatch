using MsgHatch.Domain.Failures;

namespace MsgHatch.Subscriptions.Caching;

/// <summary>
/// The invalidation of the subscription cache of one tenant (section 4.15, D126). A consumer calls it after a change of
/// the subscription data outside the library, for example a direct database update. A change through the subscription
/// store needs no call. It is a tenant service, so it never clears the cache of another tenant.
/// </summary>
public interface ISubscriptionCache
{
    /// <summary>Removes the cached subscriptions of one owner, for all message types.</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or an error of the cache backend.</returns>
    ValueTask<Error?> InvalidateAsync(SubscriptionOwner owner, CancellationToken ct);

    /// <summary>Removes all cached subscriptions of the tenant.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or an error of the cache backend.</returns>
    ValueTask<Error?> InvalidateAllAsync(CancellationToken ct);
}
