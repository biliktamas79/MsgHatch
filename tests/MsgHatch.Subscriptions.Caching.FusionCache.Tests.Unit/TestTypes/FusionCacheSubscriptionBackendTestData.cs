using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using ZiggyCreatures.Caching.Fusion;

namespace MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit.TestTypes;

/// <summary>
/// A new real in-memory FusionCache with the name of the backend, and a look into it. FusionCache has no clock option,
/// and it calculates the end of an entry with the system clock. Its <c>MemoryCache</c> ends the entry by its own clock.
/// So that clock is a fake clock that starts at the system time, not at 2030-01-01.
/// </summary>
public sealed class FusionCacheSubscriptionBackendTestData : ISubscriptionCacheBackendTestData
{
    private readonly FakeTimeProvider _time = new(TimeProvider.System.GetUtcNow());
    private readonly MemoryCache _memory;
    private readonly ServiceProvider _services;

    public FusionCacheSubscriptionBackendTestData()
    {
        _memory = new MemoryCache(new MemoryCacheOptions { Clock = new FakeSystemClock(_time) });
        var services = new ServiceCollection();
        services.AddFusionCache(FusionCacheSubscriptionBackend.CacheName).WithMemoryCache(_memory);
        _services = services.BuildServiceProvider();
    }

    public IFusionCacheProvider Caches => _services.GetRequiredService<IFusionCacheProvider>();

    public async ValueTask<bool> ContainsAsync(string key, CancellationToken ct) =>
        (await Caches.GetCache(FusionCacheSubscriptionBackend.CacheName).TryGetAsync<object>(key, null, ct)).HasValue;

    public void Advance(TimeSpan time) => _time.Advance(time);

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _memory.Dispose();
    }

    private sealed class FakeSystemClock(TimeProvider time) : ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
