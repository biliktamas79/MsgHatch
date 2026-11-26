using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Caching.FusionCache.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit.DependencyInjection;

/// <summary><c>UseFusionSubscriptionCache</c> alone registers all parts of the package (section 4.7, D60).</summary>
public sealed class FusionSubscriptionCacheExtensionsUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseFusionSubscriptionCache_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:SubscriptionCache:Expiration"] = "00:05:00",
            ["MsgHatch:SubscriptionCache:KeyPrefix"] = "default",
        });
        services.AddMsgHatch(configSectionPath: "Messaging").UseSubscriptionStore(static (_, _) => Substitute.For<ISubscriptionStore>()).UseFusionSubscriptionCache();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SubscriptionCacheOptions>>().Value;

        // Assert
        Assert.Equal((TimeSpan.FromMinutes(5), "msghatch:sub"), (options.Expiration, options.KeyPrefix));
    }

    [Fact]
    public async Task UseFusionSubscriptionCache_Alone_CachesTheSubscriptionsOfTheStoreInFusionCache()
    {
        // Arrange
        var inner = AStoreOfOneUser();
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseSubscriptionStore((_, _) => inner).UseFusionSubscriptionCache();
        await using var provider = services.Build();
        var store = provider.GetRequiredKeyedService<ISubscriptionStore>(_t1);

        // Act
        await store.FindForDeliveryAsync(new MessageType("A"), [], [new UserId("1")], Ct);
        var second = await store.FindForDeliveryAsync(new MessageType("A"), [], [new UserId("1")], Ct);

        // Assert
        Assert.Equal((1, 1), (inner.ReceivedCalls().Count(), second.Value.Count));
    }

    [Fact]
    public async Task UseFusionSubscriptionCache_Alone_RegistersTheBackendAndTheCacheOfEachKey()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseFusionSubscriptionCache();
        await using var provider = services.Build();

        // Act
        var backend = provider.GetRequiredService<ISubscriptionCacheBackend>();
        var error = await provider.GetRequiredKeyedService<ISubscriptionCache>(_t1).InvalidateAllAsync(Ct);

        // Assert
        Assert.Equal((true, (Error?)null), (backend is FusionCacheSubscriptionBackend, error));
    }

    [Fact]
    public async Task UseFusionSubscriptionCache_DefaultFusionCacheOfTheHost_StaysUnchangedAndApart()
    {
        // Arrange
        var hostDuration = TimeSpan.FromMinutes(7);
        var services = TestHost.CreateServices();
        services.AddFusionCache().WithDefaultEntryOptions(o => o.Duration = hostDuration);
        services.AddMsgHatch().UseSubscriptionStore((_, _) => AStoreOfOneUser()).UseFusionSubscriptionCache();
        await using var provider = services.Build();
        await provider.GetRequiredKeyedService<ISubscriptionStore>(_t1).FindForDeliveryAsync(new MessageType("A"), [], [new UserId("1")], Ct);

        // Act
        var hostCache = provider.GetRequiredService<IFusionCache>();

        // Assert
        Assert.Equal(
            ("FusionCache", hostDuration, false),
            (hostCache.CacheName, hostCache.DefaultEntryOptions.Duration,
                (await hostCache.TryGetAsync<IReadOnlyList<Subscription>>("msghatch:sub:t:t1:A:u:1", null, Ct)).HasValue));
    }

    [Fact]
    public async Task UseFusionSubscriptionCache_ChangeOfTheFusionCache_ReachesTheNamedCacheOnly()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(9);
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseFusionSubscriptionCache(configureFusionCache: b => b.WithDefaultEntryOptions(o => o.Duration = duration));
        await using var provider = services.Build();

        // Act
        var cache = provider.GetRequiredService<IFusionCacheProvider>().GetCache(FusionCacheSubscriptionBackend.CacheName);

        // Assert
        Assert.Equal(duration, cache.DefaultEntryOptions.Duration);
    }

    [Fact]
    public async Task UseFusionSubscriptionCache_SubscriptionCacheOptionsInCode_ReachTheOptions()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseFusionSubscriptionCache(static o => o.KeyPrefix = "app:subs");
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SubscriptionCacheOptions>>().Value;

        // Assert
        Assert.Equal("app:subs", options.KeyPrefix);
    }

    private static ISubscriptionStore AStoreOfOneUser()
    {
        var store = Substitute.For<ISubscriptionStore>();
        store.FindForDeliveryAsync(default, default!, default!, default)
            .ReturnsForAnyArgs(ErrorOr<IReadOnlyList<Subscription>>.ForValue([TestSubscriptions.A(owner: SubscriptionOwner.ForUser(new UserId("1")))]));
        return store;
    }
}
