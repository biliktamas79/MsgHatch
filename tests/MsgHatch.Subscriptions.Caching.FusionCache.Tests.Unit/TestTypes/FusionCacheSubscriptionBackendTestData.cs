using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using ZiggyCreatures.Caching.Fusion;

namespace MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit.TestTypes;

/// <summary>A new real in-memory FusionCache with the name of the backend, and a look into it.</summary>
public sealed class FusionCacheSubscriptionBackendTestData : ISubscriptionCacheBackendTestData
{
    private readonly ServiceProvider _services = NewServices();

    public IFusionCacheProvider Caches => _services.GetRequiredService<IFusionCacheProvider>();

    public async ValueTask<bool> ContainsAsync(string key, CancellationToken ct) =>
        (await Caches.GetCache(FusionCacheSubscriptionBackend.CacheName).TryGetAsync<object>(key, null, ct)).HasValue;

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static ServiceProvider NewServices()
    {
        var services = new ServiceCollection();
        services.AddFusionCache(FusionCacheSubscriptionBackend.CacheName);
        return services.BuildServiceProvider();
    }
}
