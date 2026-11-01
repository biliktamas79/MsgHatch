using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

public sealed class MessageBuilderUnitTests
{
    private const string _contentType = "Test:TestEnvelope";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    [Fact]
    public async Task WithRouter_AndAnOpenGenericRouter_TheRouterOfTheMessageTypeWinsForAKey()
    {
        // Arrange
        var router = Substitute.For<IMessageRouter<TestEvent>>();
        await using var provider = BuildWithTwoRouters(router);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var resolved = scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<TestEvent>>(_t1);

        // Assert
        Assert.Same(router, resolved);
    }

    [Fact]
    public async Task WithRouter_AndAnOpenGenericRouter_TheRouterOfTheMessageTypeWinsForNoKey()
    {
        // Arrange
        var router = Substitute.For<IMessageRouter<TestEvent>>();
        await using var provider = BuildWithTwoRouters(router);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var resolved = scope.ServiceProvider.GetRequiredService<IMessageRouter<TestEvent>>();

        // Assert
        Assert.Same(router, resolved);
    }

    [Fact]
    public async Task UseRouter_MessageTypeWithNoRouterOfItsOwn_UsesTheOpenGenericRouter()
    {
        // Arrange
        await using var provider = BuildWithTwoRouters(Substitute.For<IMessageRouter<TestEvent>>());
        await using var scope = provider.CreateAsyncScope();

        // Act
        var resolved = scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<OtherTestEvent>>(_t1);

        // Assert
        Assert.IsType<TestOpenRouter<OtherTestEvent>>(resolved);
    }

    [Fact]
    public async Task WithUpgrade_OldEventVersion_AddsAContentTypeForEachVersion()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
            .WithUpgrade(1, TestJsonContext.Default.TestEventV1, static old => new TestEvent(old.Value))
            .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()));

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.Equal(
            [
                ("Settings:Test@1", typeof(TestChannelSettings)),
                ("TestEvent@2", typeof(ReceivedMessage<TestEvent>)),
                ("TestEvent@1", typeof(ReceivedMessage<TestEventV1>)),
                ("Test:TestEnvelope@1", typeof(OutgoingMessage<TestEnvelope>)),
            ],
            provider.GetRequiredService<IContentTypeRegistry>().Entries.Select(static e => (e.Discriminator, e.ClrType)));
    }

    [Fact]
    public async Task UsePayloadLoader_LoaderType_CreatesTheLoaderWithTheRequestedKey()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false)
            .UseRouter(typeof(TestOpenRouter<>))
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .UsePayloadLoader<TestEnvelope, KeyCapturingPayloadLoader>());
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();
        var key = TenantKey.For(new TenantId("t1"));

        // Act
        var loader = scope.ServiceProvider.GetRequiredKeyedService<IPayloadLoader<TestEvent, TestEnvelope>>(key);

        // Assert
        Assert.Equal(key, Assert.IsType<KeyCapturingPayloadLoader>(loader).Key);
    }

    [Fact]
    public async Task AsChannel_ValueInTheSectionOfTheContentType_ChangesOnlyThatValue()
    {
        // Arrange
        var configuration = new Dictionary<string, string?> { ["MsgHatch:Retry:Test:TestEnvelope:MaxTryCount"] = "3" };
        await using var provider = BuildWithRetry(configuration, withRetry: null);

        // Act
        var options = provider.GetRequiredService<IOptionsMonitor<RetryOptions>>().Get(_contentType);

        // Assert
        Assert.Equal(
            (3, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(15), (TimeSpan?)null),
            (options.MaxTryCount, options.BaseDelay, options.MaxDelay, options.Expiry));
    }

    [Fact]
    public async Task WithRetry_ValueInTheSectionOfTheContentType_TheValueOfWithRetryWins()
    {
        // Arrange
        var configuration = new Dictionary<string, string?> { ["MsgHatch:Retry:Test:TestEnvelope:MaxTryCount"] = "3" };
        await using var provider = BuildWithRetry(configuration, withRetry: static o => o.MaxTryCount = 5);

        // Act
        var options = provider.GetRequiredService<IOptionsMonitor<RetryOptions>>().Get(_contentType);

        // Assert
        Assert.Equal(5, options.MaxTryCount);
    }

    [Fact]
    public async Task AsChannel_ValueInTheSectionOfTheChannel_AppliesToEachRegistrationOfTheChannel()
    {
        // Arrange
        var configuration = new Dictionary<string, string?> { ["MsgHatch:Retry:Test:BaseDelay"] = "00:00:02" };
        await using var provider = BuildWithRetry(configuration, withRetry: null);
        var monitor = provider.GetRequiredService<IOptionsMonitor<RetryOptions>>();

        // Act
        TimeSpan[] baseDelays = [monitor.Get(_contentType).BaseDelay, monitor.Get("Test:OtherEnvelope").BaseDelay];

        // Assert
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)], baseDelays);
    }

    [Fact]
    public async Task WithRetry_MaxTryCountZero_FailsTheStartupValidation()
    {
        // Arrange
        await using var provider = BuildWithRetry(new Dictionary<string, string?>(), withRetry: static o => o.MaxTryCount = 0);
        var validator = provider.GetRequiredService<IStartupValidator>();

        // Act
        var exception = Record.Exception(validator.Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    private static ServiceProvider BuildWithTwoRouters(IMessageRouter<TestEvent> router)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false)
            .AddTestChannel()
            .UseRouter(typeof(TestOpenRouter<>))
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m
                .WithRouter((_, _) => router)
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()))
            .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                .AsChannel(TestChannel.Id, "OtherEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<OtherTestEvent>()));
        return services.Build();
    }

    private static ServiceProvider BuildWithRetry(Dictionary<string, string?> configuration, Action<RetryOptions>? withRetry)
    {
        var services = TestHost.CreateServices(configuration: configuration);
        services.AddMsgHatch()
            .AddTestChannel()
            .UseInMemoryStores()
            .UseRouter(typeof(TestOpenRouter<>))
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m
                .AsChannel(
                    TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>(),
                    configure: channel => channel.WithRetry(o => withRetry?.Invoke(o))))
            .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                .AsChannel(TestChannel.Id, "OtherEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<OtherTestEvent>()));
        return services.Build();
    }
}
