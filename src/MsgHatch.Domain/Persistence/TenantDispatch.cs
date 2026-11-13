using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Persistence;

/// <summary>
/// The part that both dispatch composites share (D125, section 4.5): one store for each key, the claim that asks the
/// store of <see cref="TenantKey.None"/> and then the stores of the tenants in turn, and the calls of all stores. It
/// keeps the next due time of each tenant and lane, which is a channel of the outbox or the inbox, and a claim skips a
/// tenant whose next due time is later than now. Each <c>MaxPollInterval</c>, one claim of a lane asks all tenants.
/// </summary>
/// <typeparam name="TStore">The dispatch store of one key.</typeparam>
// ponytail: a sweep asks each tenant store, a shared claim index or a dispatcher for each group of tenants if there are many tenants
internal sealed class TenantDispatch<TStore>(
    ITenantSource tenants, Func<TenantKey, TStore> storeOf, TimeSpan maxPollInterval, ILogger logger)
    where TStore : class
{
    private readonly ConcurrentDictionary<TenantKey, TStore> _stores = new();
    private readonly Lock _lock = new();
    private readonly Dictionary<TenantKey, DueTimes> _dueTimes = [];
    private readonly Dictionary<string, DateTime> _sweptAtUtc = [];
    private readonly Dictionary<string, TenantId> _lastGivers = [];

    public TStore StoreOf(TenantKey key) => _stores.GetOrAdd(key, storeOf);

    /// <summary>
    /// A tenant store of this process added or retried messages, or a try gave a next try time, so the next claim of
    /// each lane asks the tenant.
    /// </summary>
    public void ForgetNextDue(TenantKey key)
    {
        lock (_lock)
        {
            var times = DueTimesOf(key);
            times.Version++;
            times.NextDueAtUtc.Clear();
        }
    }

    public async ValueTask<ErrorOr<ClaimResult<T>>> ClaimAsync<T>(
        string lane, int maxCount, DateTime nowUtc, Func<TStore, int, ValueTask<ErrorOr<ClaimResult<T>>>> claim, CancellationToken ct)
    {
        var listed = await tenants.GetTenantsAsync(ct);
        if (listed.IsError)
        {
            return listed.Error;
        }

        var system = await claim(StoreOf(TenantKey.None), maxCount);
        if (system.IsError || system.Value.Down is not null)
        {
            return system;
        }

        List<T> messages = [.. system.Value.Messages];
        var nextDueAtUtc = system.Value.NextDueAtUtc;
        var sweep = StartsSweep(lane, nowUtc);
        var tenantsInTurn = listed.Value;
        var first = TurnAfterLastGiver(lane, tenantsInTurn);
        for (var turn = 0; turn < tenantsInTurn.Count; turn++)
        {
            if (messages.Count >= maxCount)
            {
                return new ClaimResult<T>(messages, nowUtc);
            }

            var tenant = tenantsInTurn[(first + turn) % tenantsInTurn.Count];
            var key = TenantKey.For(tenant);
            var (skip, knownNextDueAtUtc, version) = Check(key, lane, nowUtc, sweep);
            if (skip)
            {
                nextDueAtUtc = Earliest(nextDueAtUtc, knownNextDueAtUtc);
                continue;
            }

            var result = await claim(StoreOf(key), maxCount - messages.Count);
            if (result.IsError && result.Error.Type == Errors.OperationCancelled)
            {
                return result.Error;
            }

            if (result.IsError)
            {
                LogSkippedTenant(tenant, lane, result.Error);
                continue;
            }

            Remember(key, lane, result.Value.NextDueAtUtc, version);
            nextDueAtUtc = Earliest(nextDueAtUtc, result.Value.NextDueAtUtc);
            messages.AddRange(result.Value.Messages);
            RecordGiver(lane, tenant, result.Value.Messages.Count);
        }

        return new ClaimResult<T>(messages, nextDueAtUtc);
    }

    /// <summary>Calls each store, the store of <see cref="TenantKey.None"/> first, and stops at the first error.</summary>
    public async ValueTask<ErrorOr<List<T>>> CollectAsync<T>(Func<TStore, ValueTask<ErrorOr<IReadOnlyList<T>>>> call, CancellationToken ct)
    {
        var keys = await KeysAsync(ct);
        if (keys.IsError)
        {
            return keys.Error;
        }

        List<T> items = [];
        foreach (var key in keys.Value)
        {
            var result = await call(StoreOf(key));
            if (result.IsError)
            {
                return result.Error;
            }

            items.AddRange(result.Value);
        }

        return items;
    }

    /// <summary>
    /// Deletes in each store, the store of <see cref="TenantKey.None"/> first, until <paramref name="maxCount"/> messages
    /// in total, and stops at the first error. A store gets the count that is left.
    /// </summary>
    public async ValueTask<ErrorOr<int>> DeleteAsync(int maxCount, Func<TStore, int, ValueTask<ErrorOr<int>>> delete, CancellationToken ct)
    {
        var keys = await KeysAsync(ct);
        if (keys.IsError)
        {
            return keys.Error;
        }

        var deleted = 0;
        foreach (var key in keys.Value)
        {
            var result = await delete(StoreOf(key), maxCount - deleted);
            if (result.IsError)
            {
                return result.Error;
            }

            deleted += result.Value;
            if (deleted >= maxCount)
            {
                break;
            }
        }

        return deleted;
    }

    private async ValueTask<ErrorOr<IReadOnlyList<TenantKey>>> KeysAsync(CancellationToken ct)
    {
        var listed = await tenants.GetTenantsAsync(ct);
        return listed.IsError
            ? listed.Error
            : ErrorOr<IReadOnlyList<TenantKey>>.ForValue([TenantKey.None, .. listed.Value.Select(static tenant => TenantKey.For(tenant))]);
    }

    private bool StartsSweep(string lane, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (_sweptAtUtc.TryGetValue(lane, out var sweptAtUtc) && nowUtc - sweptAtUtc < maxPollInterval)
            {
                return false;
            }

            _sweptAtUtc[lane] = nowUtc;
            return true;
        }
    }

    private int TurnAfterLastGiver(string lane, IReadOnlyList<TenantId> tenantsInTurn)
    {
        lock (_lock)
        {
            if (!_lastGivers.TryGetValue(lane, out var lastGiver))
            {
                return 0;
            }

            for (var turn = 0; turn < tenantsInTurn.Count; turn++)
            {
                if (tenantsInTurn[turn] == lastGiver)
                {
                    return turn + 1;
                }
            }

            return 0;
        }
    }

    private void RecordGiver(string lane, TenantId tenant, int givenCount)
    {
        if (givenCount == 0)
        {
            return;
        }

        lock (_lock)
        {
            _lastGivers[lane] = tenant;
        }
    }

    private (bool Skip, DateTime? NextDueAtUtc, int Version) Check(TenantKey key, string lane, DateTime nowUtc, bool sweep)
    {
        lock (_lock)
        {
            var times = DueTimesOf(key);
            var known = times.NextDueAtUtc.TryGetValue(lane, out var nextDueAtUtc);
            return (!sweep && known && !(nextDueAtUtc <= nowUtc), nextDueAtUtc, times.Version);
        }
    }

    /// <summary>Keeps the next due time of a claim, unless a change of the tenant came while the claim ran.</summary>
    private void Remember(TenantKey key, string lane, DateTime? nextDueAtUtc, int versionBeforeClaim)
    {
        lock (_lock)
        {
            var times = DueTimesOf(key);
            if (times.Version == versionBeforeClaim)
            {
                times.NextDueAtUtc[lane] = nextDueAtUtc;
            }
        }
    }

    private void LogSkippedTenant(TenantId tenant, string lane, Error error)
    {
        var level = ErrorLogLevels.For(error);
        var channel = lane.Length == 0 ? null : lane;
        logger.TenantClaimFailed(level, tenant.Value, channel, error.Type.Code, error.Type.Name, error.Message);
    }

    private DueTimes DueTimesOf(TenantKey key)
    {
        if (!_dueTimes.TryGetValue(key, out var times))
        {
            times = new DueTimes();
            _dueTimes.Add(key, times);
        }

        return times;
    }

    private static DateTime? Earliest(DateTime? first, DateTime? second) =>
        first is null ? second : second is null || first <= second ? first : second;

    /// <summary>
    /// The known next due time of each lane of one tenant: absent when the next claim must ask the tenant, and null
    /// when the tenant has no pending message. The version changes with each change of the tenant.
    /// </summary>
    private sealed class DueTimes
    {
        public int Version { get; set; }

        public Dictionary<string, DateTime?> NextDueAtUtc { get; } = [];
    }
}
