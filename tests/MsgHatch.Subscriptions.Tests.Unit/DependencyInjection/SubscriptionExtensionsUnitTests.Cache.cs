using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Caching;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.DependencyInjection;

/// <summary><c>UseSubscriptionCache</c>: the wrapper order, the options, and the selection of the backend (sections 4.7 and 4.15).</summary>
public sealed partial class SubscriptionExtensionsUnitTests
{
    public static TheoryData<string> SetupOrders => ["store first", "cache first"];

    [Fact]
    public async Task UseSubscriptionCache_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:SubscriptionCache:Expiration"] = "00:05:00",
            ["MsgHatch:SubscriptionCache:KeyPrefix"] = "default",
        });
        services.AddMsgHatch(configSectionPath: "Messaging").UseSubscriptionCache(_ => Substitute.For<ISubscriptionCacheBackend>());
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SubscriptionCacheOptions>>().Value;

        // Assert
        Assert.Equal((TimeSpan.FromMinutes(5), "msghatch:sub"), (options.Expiration, options.KeyPrefix));
    }

    [Theory]
    [MemberData(nameof(SetupOrders))]
    public async Task UseSubscriptionCache_EachSetupOrder_PutsTheCachingWrapperBetweenTheLoggingWrapperAndTheStore(string order)
    {
        // Arrange
        var inner = AStoreOfOneUser();
        var services = TestHost.CreateServices();
        SetUp(services.AddMsgHatch(), order, inner, AStatefulBackend());
        await using var provider = services.Build();
        var store = provider.GetRequiredKeyedService<ISubscriptionStore>(_t1);

        // Act
        await store.FindForDeliveryAsync(new MessageType("A"), [], [new UserId("1")], Ct);
        await store.FindForDeliveryAsync(new MessageType("A"), [], [new UserId("1")], Ct);

        // Assert
        Assert.Equal(
            (order, 1, 2),
            (order, inner.ReceivedCalls().Count(), provider.LogRecords().Count(static record => record.Id.Id == 1100)));
    }

    [Fact]
    public async Task UseSubscriptionCache_EnableLoggingFalse_StillCaches()
    {
        // Arrange
        var inner = AStoreOfOneUser();
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false).UseSubscriptionStore((_, _) => inner).UseSubscriptionCache(_ => AStatefulBackend());
        await using var provider = services.Build();
        var store = provider.GetRequiredKeyedService<ISubscriptionStore>(_t1);

        // Act
        await store.FindForDeliveryAsync(new MessageType("A"), [], [new UserId("1")], Ct);
        await store.FindForDeliveryAsync(new MessageType("A"), [], [new UserId("1")], Ct);

        // Assert
        Assert.Single(inner.ReceivedCalls());
    }

    [Fact]
    public async Task UseSubscriptionCacheOfAType_Alone_ResolvesThatBackendAsOneSingleton()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseSubscriptionCache<TypedBackend>();
        await using var provider = services.Build();

        // Act
        ISubscriptionCacheBackend[] backends = [provider.GetRequiredService<ISubscriptionCacheBackend>(), provider.GetRequiredService<ISubscriptionCacheBackend>()];

        // Assert
        Assert.Equal((true, true), (backends[0] is TypedBackend, ReferenceEquals(backends[0], backends[1])));
    }

    [Fact]
    public async Task UseSubscriptionCache_OptionsInTheConfiguration_ReachTheOptions()
    {
        // Arrange
        var configuration = new Dictionary<string, string?>
        {
            ["MsgHatch:SubscriptionCache:Expiration"] = "00:05:00", ["MsgHatch:SubscriptionCache:KeyPrefix"] = "app:subs",
        };
        var services = TestHost.CreateServices(configuration: configuration);
        services.AddMsgHatch().UseSubscriptionCache(_ => Substitute.For<ISubscriptionCacheBackend>());
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SubscriptionCacheOptions>>().Value;

        // Assert
        Assert.Equal((TimeSpan.FromMinutes(5), "app:subs"), (options.Expiration, options.KeyPrefix));
    }

    [Fact]
    public async Task UseSubscriptionCache_KeyPrefixInCodeAndInTheConfiguration_TheValueOfTheCodeWins()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:SubscriptionCache:KeyPrefix"] = "config" });
        services.AddMsgHatch().UseSubscriptionCache(_ => Substitute.For<ISubscriptionCacheBackend>(), static o => o.KeyPrefix = "code");
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SubscriptionCacheOptions>>().Value;

        // Assert
        Assert.Equal("code", options.KeyPrefix);
    }

    [Fact]
    public async Task Validate_InvalidKeyPrefix_ThrowsOptionsValidationExceptionForKeyPrefix()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseInMemoryStores()
            .UseSubscriptionCache(_ => Substitute.For<ISubscriptionCacheBackend>(), static o => o.KeyPrefix = "msghatch sub");
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var validation = Assert.IsType<OptionsValidationException>(exception);
        var failure = Assert.Single(validation.Failures);
        Assert.Equal(
            (typeof(SubscriptionCacheOptions), nameof(SubscriptionCacheOptions.KeyPrefix)),
            (validation.OptionsType, failure[..failure.IndexOf(':', StringComparison.Ordinal)]));
    }

    [Fact]
    public async Task UseSubscriptionCache_SecondSelection_FailsTheStartupValidationAndNamesBothMethods()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseInMemoryStores()
            .UseSubscriptionCache(_ => Substitute.For<ISubscriptionCacheBackend>(), setupMethod: "UseFirstSubscriptionCache")
            .UseSubscriptionCache(_ => Substitute.For<ISubscriptionCacheBackend>());
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            ["UseFirstSubscriptionCache and UseSubscriptionCache select an implementation of ISubscriptionCacheBackend."],
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void SetUp(MsgHatchBuilder builder, string order, ISubscriptionStore inner, ISubscriptionCacheBackend backend) =>
        _ = order == "store first"
            ? builder.UseSubscriptionStore((_, _) => inner).UseSubscriptionCache(_ => backend)
            : builder.UseSubscriptionCache(_ => backend).UseSubscriptionStore((_, _) => inner);

    private static ISubscriptionStore AStoreOfOneUser()
    {
        var store = Substitute.For<ISubscriptionStore>();
        store.FindForDeliveryAsync(default, default!, default!, default)
            .ReturnsForAnyArgs(ErrorOr<IReadOnlyList<Subscription>>.ForValue([TestSubscriptions.A(owner: SubscriptionOwner.ForUser(new UserId("1")))]));
        return store;
    }

    private static ISubscriptionCacheBackend AStatefulBackend()
    {
        var entries = new ConcurrentDictionary<string, IReadOnlyList<Subscription>>(StringComparer.Ordinal);
        var backend = Substitute.For<ISubscriptionCacheBackend>();
        backend.TryGetAsync(default!, default)
            .ReturnsForAnyArgs(call => ErrorOr<IReadOnlyList<Subscription>?>.ForValue(entries.GetValueOrDefault(call.ArgAt<string>(0))));
        backend.SetAsync(default!, default!, default!, default, default)
            .ReturnsForAnyArgs(call => Remember(entries, call.ArgAt<string>(0), call.ArgAt<IReadOnlyList<Subscription>>(1)));
        return backend;
    }

    private static Error? Remember(ConcurrentDictionary<string, IReadOnlyList<Subscription>> entries, string key, IReadOnlyList<Subscription> subscriptions)
    {
        entries[key] = subscriptions;
        return null;
    }

    private sealed class TypedBackend : ISubscriptionCacheBackend
    {
        public ValueTask<ErrorOr<IReadOnlyList<Subscription>?>> TryGetAsync(string key, CancellationToken ct) => throw new NotSupportedException();

        public ValueTask<Error?> SetAsync(
            string key, IReadOnlyList<Subscription> subscriptions, IReadOnlyList<string> tags, TimeSpan expiration, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<Error?> RemoveByTagAsync(string tag, CancellationToken ct) => throw new NotSupportedException();
    }
}
