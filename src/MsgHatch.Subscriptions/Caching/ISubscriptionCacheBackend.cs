using MsgHatch.Domain.Failures;

namespace MsgHatch.Subscriptions.Caching;

/// <summary>
/// The cache of the subscription caching wrapper (D39). The packages <c>MsgHatch.Subscriptions.Caching.Hybrid</c> and
/// <c>MsgHatch.Subscriptions.Caching.FusionCache</c> implement it. It is a thread-safe singleton for all tenants, because
/// each key and tag holds its tenant (D114). Each method returns an error for a failure and does not throw.
/// </summary>
public interface ISubscriptionCacheBackend
{
    /// <summary>Gives the subscriptions of a key, or null for a miss. A miss stores nothing.</summary>
    /// <param name="key">The key.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The subscriptions, null, or an error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<Subscription>?>> TryGetAsync(string key, CancellationToken ct);

    /// <summary>Keeps the subscriptions of a key, also an empty list, with tags and an expiration.</summary>
    /// <param name="key">The key.</param>
    /// <param name="subscriptions">The subscriptions. The caller does not change the list after the call.</param>
    /// <param name="tags">The tags, which <see cref="RemoveByTagAsync"/> uses.</param>
    /// <param name="expiration">The time that the cache keeps the entry.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or an error.</returns>
    ValueTask<Error?> SetAsync(
        string key, IReadOnlyList<Subscription> subscriptions, IReadOnlyList<string> tags, TimeSpan expiration,
        CancellationToken ct);

    /// <summary>Removes each entry with the tag.</summary>
    /// <param name="tag">The tag.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or an error.</returns>
    ValueTask<Error?> RemoveByTagAsync(string tag, CancellationToken ct);
}
