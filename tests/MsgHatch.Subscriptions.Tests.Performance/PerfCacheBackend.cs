using System.Collections.Concurrent;
using MsgHatch.Domain.Failures;
using MsgHatch.Subscriptions.Caching;

namespace MsgHatch.Subscriptions.Tests.Performance;

/// <summary>
/// The cache backend of the benchmarks, on a dictionary in memory with no expiration. A hit gives the stored list with
/// no copy, so the backend allocates nothing on a hit.
/// </summary>
public sealed class PerfCacheBackend : ISubscriptionCacheBackend
{
    private readonly ConcurrentDictionary<string, ValueTask<ErrorOr<IReadOnlyList<Subscription>?>>> _entries = new(StringComparer.Ordinal);

    public ValueTask<ErrorOr<IReadOnlyList<Subscription>?>> TryGetAsync(string key, CancellationToken ct) =>
        _entries.TryGetValue(key, out var hit) ? hit : ValueTask.FromResult(ErrorOr<IReadOnlyList<Subscription>?>.ForValue(null));

    public ValueTask<Error?> SetAsync(
        string key, IReadOnlyList<Subscription> subscriptions, IReadOnlyList<string> tags, TimeSpan expiration, CancellationToken ct)
    {
        _entries[key] = ValueTask.FromResult(ErrorOr<IReadOnlyList<Subscription>?>.ForValue(subscriptions));
        return ValueTask.FromResult<Error?>(null);
    }

    public ValueTask<Error?> RemoveByTagAsync(string tag, CancellationToken ct)
    {
        _entries.Clear();
        return ValueTask.FromResult<Error?>(null);
    }
}
