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
    private const string _section = RabbitMqSignalOptions.SectionName;

    [Fact]
    public async Task UseRabbitMqDispatchSignal_SettingsInTheSection_ReachTheOptions()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            [$"{_section}:Uri"] = "amqp://rabbit.example.com:5672/section",
            [$"{_section}:ExchangeName"] = "from-section",
        });
        services.AddMsgHatch().UseRabbitMqDispatchSignal();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<RabbitMqSignalOptions>>().Value;

        // Assert
        Assert.Equal((new Uri("amqp://rabbit.example.com:5672/section"), "from-section"), (options.Uri, options.ExchangeName));
    }

    [Fact]
    public async Task UseRabbitMqDispatchSignal_SettingsFromCode_WinOverTheSection()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            [$"{_section}:Uri"] = "amqp://rabbit.example.com:5672/section",
            [$"{_section}:ExchangeName"] = "from-section",
        });
        services.AddMsgHatch().UseRabbitMqDispatchSignal(static o => (o.Uri, o.ExchangeName) = (new Uri("amqp://localhost/code"), "from-code"));
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<RabbitMqSignalOptions>>().Value;

        // Assert
        Assert.Equal((new Uri("amqp://localhost/code"), "from-code"), (options.Uri, options.ExchangeName));
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
