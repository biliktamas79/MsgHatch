using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit.TestTypes;

/// <summary>A new real in-memory HybridCache, and a look into it that stores nothing.</summary>
public sealed class HybridCacheSubscriptionBackendTestData : ISubscriptionCacheBackendTestData
{
    private static readonly HybridCacheEntryOptions _readOnly = new()
    {
        Flags = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite,
    };

    private readonly ServiceProvider _services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();

    public HybridCache Cache => _services.GetRequiredService<HybridCache>();

    public async ValueTask<bool> ContainsAsync(string key, CancellationToken ct)
    {
        var factoryRan = false;
        await Cache.GetOrCreateAsync(
            key,
            _ =>
            {
                factoryRan = true;
                return ValueTask.FromResult<HybridCacheSubscriptionEntry?>(null);
            },
            _readOnly,
            null,
            ct);
        return !factoryRan;
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
