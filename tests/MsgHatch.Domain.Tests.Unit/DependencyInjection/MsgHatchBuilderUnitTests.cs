using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

public sealed class MsgHatchBuilderUnitTests
{
    public static TheoryData<Type> TypesThatAreNotOpenGenericRouters =>
    [
        typeof(TestOpenRouter<TestEvent>),
        typeof(string),
        typeof(List<>),
        typeof(ListTestOpenRouter<>),
        typeof(FixedEventTestOpenRouter<>),
    ];

    [Fact]
    public async Task ConfigureSharedOptions_SectionThatALaterSetupMethodBinds_TheValueFromCodeWins()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["Test:Shared:Value"] = "section" });
        services.AddMsgHatch().ConfigureSharedOptions<SharedTestOptions>("UseFirstStores", static o => o.Value = "code");
        services.AddOptions<SharedTestOptions>().BindConfiguration(SharedTestOptions.SectionName);
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SharedTestOptions>>().Value;

        // Assert
        Assert.Equal("code", options.Value);
    }

    [Fact]
    public async Task AddExceptionClassifier_TwoClassifiersOfOneClient_RegistersBothInTheirOrder()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch()
            .AddExceptionClassifier<TestClient, UnknownClassifier<TestClient>>()
            .AddExceptionClassifier<TestClient, ConflictClassifier<TestClient>>();

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.Equal(
            [typeof(UnknownClassifier<TestClient>), typeof(ConflictClassifier<TestClient>)],
            provider.GetServices<IExceptionClassifier<TestClient>>().Select(static classifier => classifier.GetType()));
    }

    [Fact]
    public async Task AddExceptionClassifier_SameClassifierTwice_RegistersItOneTime()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch()
            .AddExceptionClassifier<TestClient, ConflictClassifier<TestClient>>()
            .AddExceptionClassifier<TestClient, ConflictClassifier<TestClient>>();

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.Single(provider.GetServices<IExceptionClassifier<TestClient>>());
    }

    [Fact]
    public async Task AddExceptionClassifier_ClassifierOfAnotherClient_IsNoClassifierOfThisClient()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddExceptionClassifier<OtherTestClient, ConflictClassifier<OtherTestClient>>();

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.Equal(
            (0, 1),
            (provider.GetServices<IExceptionClassifier<TestClient>>().Count(), provider.GetServices<IExceptionClassifier<OtherTestClient>>().Count()));
    }

    [Fact]
    public async Task AddChannelType_NewChannel_AddsTheChannelAndItsSettingsContentType()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel();

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.Same(TestChannel.Descriptor, provider.GetRequiredService<IMessageRegistry>().FindChannel(TestChannel.Id));
        Assert.True(provider.GetRequiredService<IContentTypeRegistry>().TryFind("Settings:Test", 1, out var entry));
        Assert.Equal(("Settings:Test@1", typeof(TestChannelSettings)), (entry.Discriminator, entry.ClrType));
    }

    [Fact]
    public async Task AddChannelType_SecondCallForTheSameChannel_ChangesNothing()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var second = TestChannel.Descriptor with { Name = "Second" };
        services.AddMsgHatch()
            .AddTestChannel()
            .AddChannelType(second, 2, TestJsonContext.Default.TestChannelSettings, Substitute.For<IChannelSettingsValidator>());

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.Equal([TestChannel.Descriptor], provider.GetRequiredService<IMessageRegistry>().Channels);
        Assert.Equal(
            ["Settings:Test@1"],
            provider.GetRequiredService<IContentTypeRegistry>().Entries.Select(static e => e.Discriminator));
    }

    [Fact]
    public async Task AddTraceType_TraceType_AddsTheTraceContentType()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTraceType("Test", 1, TestJsonContext.Default.TestTrace);

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.True(provider.GetRequiredService<IContentTypeRegistry>().TryFind("Trace:Test", 1, out var entry));
        Assert.Equal(("Trace:Test@1", typeof(TestTrace)), (entry.Discriminator, entry.ClrType));
    }

    [Fact]
    public void AddTraceType_InvalidName_ThrowsArgumentException()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() => builder.AddTraceType("Sub-scription", 1, TestJsonContext.Default.TestTrace));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Fact]
    public void AddMessage_InvalidName_ThrowsArgumentException()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() =>
            builder.AddMessage("Test Event", 1, TestJsonContext.Default.TestEvent, static _ => { }));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Fact]
    public void AsChannel_InvalidName_ThrowsArgumentException()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch().AddTestChannel();

        // Act
        var exception = Record.Exception(() =>
            builder.AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m.AsChannel(
                TestChannel.Id, "Test:Envelope", 1, TestJsonContext.Default.TestEnvelope,
                static (_, _) => Substitute.For<IEnvelopeComposer<TestEvent, TestEnvelope>>())));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Fact]
    public async Task UseOutboxStore_StoreType_CreatesTheStoreWithTheRequestedKey()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false).UseOutboxStore<KeyCapturingOutboxStore>();
        await using var provider = services.Build();
        var key = TenantKey.For(new TenantId("t1"));

        // Act
        var store = provider.GetRequiredKeyedService<IOutboxStore>(key);

        // Assert
        Assert.Equal(key, Assert.IsType<KeyCapturingOutboxStore>(store).Key);
    }

    [Fact]
    public async Task UseOutboxDispatchStore_StoreType_CreatesOneStoreOfThatType()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false).UseOutboxDispatchStore<TestOutboxDispatchStore>();
        await using var provider = services.Build();
        var first = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        var second = provider.GetRequiredService<IOutboxDispatchStore>();

        // Assert
        Assert.IsType<TestOutboxDispatchStore>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task UseRegistrySettingsStore_StoreType_CreatesOneStoreOfThatType()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false).UseRegistrySettingsStore<InMemoryRegistrySettingsStore>();
        await using var provider = services.Build();
        var first = provider.GetRequiredService<IRegistrySettingsStore>();

        // Act
        var second = provider.GetRequiredService<IRegistrySettingsStore>();

        // Assert
        Assert.IsType<InMemoryRegistrySettingsStore>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task UseInMemoryStores_NoOtherStore_RegistersTheInMemoryRegistrySettingsStore()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false).UseInMemoryStores();
        await using var provider = services.Build();

        // Act
        var store = provider.GetRequiredService<IRegistrySettingsStore>();

        // Assert
        Assert.Same(provider.GetRequiredService<InMemoryRegistrySettingsStore>(), store);
    }

    [Fact]
    public async Task AddTenantService_ServiceOfAPackage_GivesTheInstanceOfEachKeyAndTheInstanceOfNoneWithNoKey()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTenantService(ServiceLifetime.Singleton, static (_, key) => new KeyCapturingService(key));
        await using var provider = services.Build();
        var t1 = TenantKey.For(new TenantId("t1"));
        var none = provider.GetRequiredKeyedService<KeyCapturingService>(TenantKey.None);

        // Act
        KeyCapturingService[] resolved = [provider.GetRequiredKeyedService<KeyCapturingService>(t1), provider.GetRequiredService<KeyCapturingService>()];

        // Assert
        Assert.Equal([t1, TenantKey.None], resolved.Select(static service => service.Key));
        Assert.Same(none, resolved[1]);
    }

    [Fact]
    public async Task AddTenantService_FactoryThatResolvesAnotherTenantService_GetsTheServiceOfTheSameKey()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch()
            .AddTenantService(ServiceLifetime.Singleton, static (_, key) => new KeyCapturingService(key))
            .AddTenantService(
                ServiceLifetime.Singleton,
                static (tenantServices, _) => new DependentService(tenantServices.GetRequiredService<KeyCapturingService>()));
        await using var provider = services.Build();
        var t1 = TenantKey.For(new TenantId("t1"));

        // Act
        var service = provider.GetRequiredKeyedService<DependentService>(t1);

        // Assert
        Assert.Equal(t1, service.Inner.Key);
    }

    [Theory]
    [InlineData(true, "wrapper")]
    [InlineData(false, "inner")]
    public async Task AddTenantService_LoggingWrapper_WrapsTheServiceOnlyWithEnableLogging(bool enableLogging, string expectedName)
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).AddTenantService(
            ServiceLifetime.Singleton, static (_, key) => new KeyCapturingService(key), static (_, _, inner) => inner with { Name = "wrapper" });
        await using var provider = services.Build();

        // Act
        var service = provider.GetRequiredService<KeyCapturingService>();

        // Assert
        Assert.Equal(expectedName, service.Name);
    }

    [Fact]
    public async Task AddTenantService_FactoryThatResolvesAKeyedService_GetsTheServiceOfThatKey()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddKeyedSingleton(KeyedService.AnyKey, static (_, _) => new InMemoryOutboxStore());
        services.AddMsgHatch(static o => o.EnableLogging = false)
            .UseOutboxStore(static (tenantServices, key) => tenantServices.GetRequiredKeyedService<InMemoryOutboxStore>(key).For(key));
        await using var provider = services.Build();
        var message = TestMessages.AnOutgoingMessage(tenant: _t1.Tenant, channel: TestChannel.Id);

        // Act
        await provider.GetRequiredKeyedService<IOutboxStore>(_t1).AddAsync([message], Ct);

        // Assert
        Assert.Equal(
            [message.Delivery.Id],
            provider.GetRequiredKeyedService<InMemoryOutboxStore>(_t1).GetMessages().Select(static stored => stored.Delivery.Id));
    }

    [Fact]
    public async Task UseStorageForEachTenant_LoggingEnabled_ResolvesTheTenantSourceAndEachStoreAsItsLoggingWrapper()
    {
        // Arrange
        await using var provider = BuildForEachTenant(enableLogging: true);
        await provider.GetRequiredKeyedService<IInboxStore>(_t1).AddAsync(TestMessages.AReceivedMessage(new TestEvent("text"), _t1.Tenant), Ct);
        await provider.GetRequiredKeyedService<IOutboxStore>(_t1).AddAsync([TestMessages.AnOutgoingMessage(tenant: _t1.Tenant, channel: TestChannel.Id)], Ct);

        // Act
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal((1, 1), (inbox.Value.Messages.Count, outbox.Value.Messages.Count));
        Assert.Equal([1200, 1202, 1300, 1304, 1500, 1500], provider.LogRecords().Select(static record => record.Id.Id).Order());
    }

    [Fact]
    public async Task UseStorageForEachTenant_TwoClaims_CallsTheDispatchStoreFunctionOneTimeForEachKey()
    {
        // Arrange
        List<TenantKey> calls = [];
        await using var provider = BuildForEachTenant(enableLogging: false, calls);
        var store = provider.GetRequiredService<IOutboxDispatchStore>();
        await store.ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Act
        await store.ClaimDueAsync(TestChannel.SecondId, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal([TenantKey.None, _t1], calls);
    }

    [Fact]
    public async Task UseStorageForEachTenant_AfterUseInMemoryStores_NamesBothSetupMethodsForEachStoreInterface()
    {
        // Arrange
        const string names = "UseInMemoryStores and UseStorageForEachTenant select an implementation of ";
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseStorageForEachTenant<TestTenantSource>(
            static (_, key) => new InMemoryInboxStore().For(key), static (_, _) => new InMemoryInboxStore(),
            static (_, key) => new InMemoryOutboxStore().For(key), static (_, _) => new InMemoryOutboxStore());
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            ["IInboxDispatchStore", "IInboxStore", "IOutboxDispatchStore", "IOutboxStore"],
            Assert.IsType<OptionsValidationException>(exception).Failures
                .Where(static failure => failure.StartsWith(names, StringComparison.Ordinal))
                .Select(static failure => failure[names.Length..^1])
                .Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TypesThatAreNotOpenGenericRouters))]
    public void UseRouter_TypeThatIsNotAnOpenGenericRouter_ThrowsArgumentException(Type routerType)
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() => builder.UseRouter(routerType));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Now => TestHost.Start.UtcDateTime;

    /// <summary>A host with an in-memory store for each key, as keyed singletons, and a tenant source with the tenant t1.</summary>
    private static ServiceProvider BuildForEachTenant(bool enableLogging, List<TenantKey>? outboxDispatchCalls = null)
    {
        var source = Substitute.For<ITenantSource>();
        source.GetTenantsAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TenantId>>.ForValue([_t1.Tenant!.Value])));
        var services = TestHost.CreateServices();
        services.AddKeyedSingleton(KeyedService.AnyKey, static (_, _) => new InMemoryInboxStore());
        services.AddKeyedSingleton(KeyedService.AnyKey, static (_, _) => new InMemoryOutboxStore());
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseStorageForEachTenant(
            _ => source,
            static (tenantServices, key) => tenantServices.GetRequiredKeyedService<InMemoryInboxStore>(key).For(key),
            static (services, key) => services.GetRequiredKeyedService<InMemoryInboxStore>(key),
            static (tenantServices, key) => tenantServices.GetRequiredKeyedService<InMemoryOutboxStore>(key).For(key),
            (services, key) =>
            {
                outboxDispatchCalls?.Add(key);
                return services.GetRequiredKeyedService<InMemoryOutboxStore>(key);
            });
        return services.Build();
    }
}
