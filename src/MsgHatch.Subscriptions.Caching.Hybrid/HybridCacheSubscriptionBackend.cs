using Microsoft.Extensions.Caching.Hybrid;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Subscriptions.Caching.Hybrid;

/// <summary>
/// The subscription cache backend on <see cref="HybridCache"/> (section 4.15, D39). It keeps each list in a
/// <see cref="HybridCacheSubscriptionEntry"/> (D152). Each exception of HybridCache becomes an <see cref="Error"/>. It
/// is thread-safe.
/// </summary>
/// <param name="cache">The HybridCache of the host.</param>
public sealed class HybridCacheSubscriptionBackend(HybridCache cache) : ISubscriptionCacheBackend
{
    private static readonly HybridCacheEntryOptions _readOnly = new()
    {
        Flags = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite,
    };

    /// <inheritdoc />
    public async ValueTask<ErrorOr<IReadOnlyList<Subscription>?>> TryGetAsync(string key, CancellationToken ct)
    {
        try
        {
            var entry = await cache.GetOrCreateAsync(key, static _ => ValueTask.FromResult<HybridCacheSubscriptionEntry?>(null), _readOnly, null, ct);
            return ErrorOr<IReadOnlyList<Subscription>?>.ForValue(entry?.Subscriptions);
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
            await cache.SetAsync(key, new HybridCacheSubscriptionEntry(subscriptions), new HybridCacheEntryOptions { Expiration = expiration }, tags, ct);
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
            await cache.RemoveByTagAsync(tag, ct);
            return null;
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    private static Error ErrorOf(Exception exception, CancellationToken ct) =>
        exception is OperationCanceledException && ct.IsCancellationRequested
            ? new Error(Errors.OperationCancelled, "The HybridCache call was cancelled.", exception)
            : new Error(Errors.Unavailable, $"HybridCache failed with {exception.GetType().Name}.", exception);
}
