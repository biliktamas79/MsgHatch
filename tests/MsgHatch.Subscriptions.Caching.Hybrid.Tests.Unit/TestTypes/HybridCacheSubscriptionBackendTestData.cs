using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit.TestTypes;

/// <summary>
/// A new real in-memory HybridCache, and a look into it that stores nothing. HybridCache ends a local entry by the clock
/// of its <c>MemoryCache</c>, not by the <see cref="TimeProvider"/> of DI, so the memory cache gets the fake clock.
/// </summary>
public sealed class HybridCacheSubscriptionBackendTestData : ISubscriptionCacheBackendTestData
{
    private static readonly HybridCacheEntryOptions _readOnly = new()
    {
        Flags = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite,
    };

    private readonly FakeTimeProvider _time = TestHost.NewFakeTime();
    private readonly ServiceProvider _services;

    public HybridCacheSubscriptionBackendTestData()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache(o => o.Clock = new FakeSystemClock(_time));
        services.AddHybridCache();
        _services = services.BuildServiceProvider();
    }

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

    public void Advance(TimeSpan time) => _time.Advance(time);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private sealed class FakeSystemClock(TimeProvider time) : ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
