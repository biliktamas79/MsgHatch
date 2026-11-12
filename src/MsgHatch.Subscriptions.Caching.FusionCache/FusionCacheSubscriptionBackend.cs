using MsgHatch.Domain.Failures;
using ZiggyCreatures.Caching.Fusion;

namespace MsgHatch.Subscriptions.Caching.FusionCache;

/// <summary>
/// The subscription cache backend on the named FusionCache <see cref="CacheName"/> (section 4.15, D39). FusionCache keeps
/// each list in memory with no serializer. With a FusionCache backplane, an invalidation also reaches the other
/// instances. Each exception of FusionCache becomes an <see cref="Error"/>. It is thread-safe.
/// </summary>
/// <param name="caches">The FusionCache provider of the host.</param>
public sealed class FusionCacheSubscriptionBackend(IFusionCacheProvider caches) : ISubscriptionCacheBackend
{
    /// <summary>The name of the FusionCache of the subscriptions. The default FusionCache of the host stays apart.</summary>
    public const string CacheName = "MsgHatch.Subscriptions";

    private readonly IFusionCache _cache = caches.GetCache(CacheName);

    /// <inheritdoc />
    public async ValueTask<ErrorOr<IReadOnlyList<Subscription>?>> TryGetAsync(string key, CancellationToken ct)
    {
        try
        {
            var cached = await _cache.TryGetAsync<IReadOnlyList<Subscription>>(key, null, ct);
            return ErrorOr<IReadOnlyList<Subscription>?>.ForValue(cached.HasValue ? cached.Value : null);
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    /// <inheritdoc />
    public async ValueTask<Error?> SetAsync(
        string key, IReadOnlyList<Subscription> subscriptions, IReadOnlyList<string> tags, TimeSpan expiration,
        CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(key, subscriptions, new FusionCacheEntryOptions(expiration), tags, ct);
            return null;
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    /// <inheritdoc />
    public async ValueTask<Error?> RemoveByTagAsync(string tag, CancellationToken ct)
    {
        try
        {
            await _cache.RemoveByTagAsync(tag, null, ct);
            return null;
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    private static Error ErrorOf(Exception exception, CancellationToken ct) =>
        exception is OperationCanceledException && ct.IsCancellationRequested
            ? new Error(Errors.OperationCancelled, "The FusionCache call was cancelled.", exception)
            : new Error(Errors.Unavailable, $"FusionCache failed with {exception.GetType().Name}.", exception);
}
