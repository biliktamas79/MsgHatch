using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
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
}
