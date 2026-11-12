using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Caching.Hybrid.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit.DependencyInjection;

/// <summary><c>UseHybridSubscriptionCache</c> alone registers all parts of the package (section 4.7, D60).</summary>
public sealed class HybridSubscriptionCacheExtensionsUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseHybridSubscriptionCache_Alone_CachesTheSubscriptionsOfTheStoreInHybridCache()
    {
        // Arrange
        var inner = AStoreOfOneUser();
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseSubscriptionStore((_, _) => inner).UseHybridSubscriptionCache();
        await using var provider = services.Build();
        var store = provider.GetRequiredKeyedService<ISubscriptionStore>(_t1);

        // Act
        await store.FindForDeliveryAsync(new MessageType("A"), [], [new UserId("1")], Ct);
        var second = await store.FindForDeliveryAsync(new MessageType("A"), [], [new UserId("1")], Ct);

        // Assert
        Assert.Equal((1, 1), (inner.ReceivedCalls().Count(), second.Value.Count));
    }

    [Fact]
    public async Task UseHybridSubscriptionCache_Alone_RegistersTheBackendAndTheCacheOfEachKey()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseHybridSubscriptionCache();
        await using var provider = services.Build();

        // Act
        var backend = provider.GetRequiredService<ISubscriptionCacheBackend>();
        var error = await provider.GetRequiredKeyedService<ISubscriptionCache>(_t1).InvalidateAllAsync(Ct);

        // Assert
        Assert.Equal((true, (Error?)null), (backend is HybridCacheSubscriptionBackend, error));
    }

    [Fact]
    public async Task UseHybridSubscriptionCache_HybridCacheOfTheHost_KeepsIt()
    {
        // Arrange
        var hostCache = Substitute.For<HybridCache>();
        var services = TestHost.CreateServices();
        services.AddSingleton(hostCache);
        services.AddMsgHatch().UseHybridSubscriptionCache();
        await using var provider = services.Build();

        // Act
        var cache = provider.GetRequiredService<HybridCache>();

        // Assert
        Assert.Same(hostCache, cache);
    }

    [Fact]
    public async Task UseHybridSubscriptionCache_OptionsOfHybridCache_ReachHybridCache()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseHybridSubscriptionCache(configureHybridCache: static o => o.MaximumKeyLength = 2048);
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;

        // Assert
        Assert.Equal(2048, options.MaximumKeyLength);
    }

    [Fact]
    public async Task UseHybridSubscriptionCache_SubscriptionCacheOptionsInCode_ReachTheOptions()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseHybridSubscriptionCache(static o => o.KeyPrefix = "app:subs");
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
