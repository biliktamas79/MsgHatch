using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Dispatch.RabbitMq.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Dispatch.RabbitMq.Tests.Unit.DependencyInjection;

/// <summary><c>UseRabbitMqDispatchSignal</c>, its options, and its startup checks (section 4.7, D305).</summary>
public sealed class RabbitMqSignalExtensionsUnitTests
{
    private const string _section = MsgHatchOptions.DefaultSectionKey + ":" + RabbitMqSignalOptions.SectionKey;

    [Fact]
    public async Task UseRabbitMqDispatchSignal_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        const string uri = "amqp://rabbit.example.com:5672/root";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:RabbitMqSignal:Uri"] = uri,
            ["MsgHatch:RabbitMqSignal:ExchangeName"] = "from-default-root",
        });
        services.AddMsgHatch(configSectionPath: "Messaging").UseRabbitMqDispatchSignal();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<RabbitMqSignalOptions>>().Value;

        // Assert
        Assert.Equal((new Uri(uri), "msghatch.dispatch"), (options.Uri, options.ExchangeName));
    }

    [Fact]
    public async Task UseRabbitMqDispatchSignal_SettingsInTheSection_ReachTheOptions()
    {
        // Arrange
        const string uri = "amqp://rabbit.example.com:5672/section";
        const string exchangeName = "from-section";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            [$"{_section}:Uri"] = uri,
            [$"{_section}:ExchangeName"] = exchangeName,
        });
        services.AddMsgHatch().UseRabbitMqDispatchSignal();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<RabbitMqSignalOptions>>().Value;

        // Assert
        Assert.Equal((new Uri(uri), exchangeName), (options.Uri, options.ExchangeName));
    }

    [Fact]
    public async Task UseRabbitMqDispatchSignal_SettingsFromCode_WinOverTheSection()
    {
        // Arrange
        const string uri = "amqp://localhost/code";
        const string exchangeName = "from-code";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            [$"{_section}:Uri"] = "amqp://rabbit.example.com:5672/section",
            [$"{_section}:ExchangeName"] = "from-section",
        });
        services.AddMsgHatch().UseRabbitMqDispatchSignal(static o => (o.Uri, o.ExchangeName) = (new Uri(uri), exchangeName));
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<RabbitMqSignalOptions>>().Value;

        // Assert
        Assert.Equal((new Uri(uri), exchangeName), (options.Uri, options.ExchangeName));
    }

    [Fact]
    public async Task UseRabbitMqDispatchSignal_NoExchangeName_UsesTheExchangeMsgHatchDispatch()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [$"{_section}:Uri"] = "amqp://localhost" });
        services.AddMsgHatch().UseRabbitMqDispatchSignal();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<RabbitMqSignalOptions>>().Value;

        // Assert
        Assert.Equal("msghatch.dispatch", options.ExchangeName);
    }

    [Fact]
    public async Task Validate_CompleteSetup_Succeeds()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores().UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .UseRabbitMqDispatchSignal(static o => o.Uri = new Uri("amqp://localhost"));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Inbox")]
    [InlineData("Outbox")]
    public async Task Validate_NoPollingForOneDispatcher_ThrowsOptionsValidationExceptionForItsOptions(string dispatcher)
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores().UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .UseRabbitMqDispatchSignal(static o => o.Uri = new Uri("amqp://localhost"))
            .Services.Configure<DispatchOptions>(dispatcher, static o => o.EnablePolling = false);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal((typeof(DispatchOptions), dispatcher), (failure.OptionsType, failure.OptionsName));
        Assert.StartsWith("UseRabbitMqDispatchSignal needs EnablePolling ", Assert.Single(failure.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_NoUri_ThrowsOptionsValidationExceptionForTheSignalOptions()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores().UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .UseRabbitMqDispatchSignal();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal(typeof(RabbitMqSignalOptions), failure.OptionsType);
        Assert.Contains(failure.Failures, static text => text.Contains(nameof(RabbitMqSignalOptions.Uri), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_AfterUseDispatchSignal_NamesBothSetupMethods()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores().UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .UseDispatchSignal(static _ => new InProcessDispatchSignal())
            .UseRabbitMqDispatchSignal(static o => o.Uri = new Uri("amqp://localhost"));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.StartsWith("UseDispatchSignal and UseRabbitMqDispatchSignal select ", failure, StringComparison.Ordinal);
    }
}
