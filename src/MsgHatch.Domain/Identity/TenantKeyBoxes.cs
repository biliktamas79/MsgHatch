using System.Collections.Concurrent;

namespace MsgHatch.Domain.Identity;

/// <summary>
/// The boxes of the tenant keys for the keyed service lookups of the event path (D280). A keyed lookup takes an
/// <see cref="object"/>, so each lookup with a <see cref="TenantKey"/> boxes the key again. This class makes the box of
/// each key one time. Two boxes of one key are equal, so a box of another lookup finds the same service.
/// </summary>
internal static class TenantKeyBoxes
{
    private const int _maxTenants = 1_024;

    private static readonly object _none = TenantKey.None;
    private static readonly ConcurrentDictionary<TenantId, object> _boxes = new();
    private static int _added;

    /// <summary>Gives the box of <paramref name="key"/>.</summary>
    public static object Of(TenantKey key)
    {
        if (key.Tenant is not { } tenant)
        {
            return _none;
        }

        if (_boxes.TryGetValue(tenant, out var box))
        {
            return box;
        }

        // ponytail: at most 1,024 tenant boxes, so a lookup of each further tenant boxes its key again. Use a bounded LRU when a host has more tenants.
        box = key;
        if (Volatile.Read(ref _added) >= _maxTenants)
        {
            return box;
        }

        if (_boxes.TryAdd(tenant, box))
        {
            Interlocked.Increment(ref _added);
            return box;
        }

        return _boxes[tenant];
    }
}
