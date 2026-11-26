using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Dispatch.NamedPipes.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Dispatch.NamedPipes.Tests.Unit.DependencyInjection;

/// <summary><c>UseNamedPipeDispatchSignal</c> and its startup checks (section 4.7, D189, D190).</summary>
public sealed class NamedPipeSignalExtensionsUnitTests
{
    private const string _prefix = "msghatch-test";

    [Fact]
    public async Task UseNamedPipeDispatchSignal_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        const string prefix = "from-root";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:NamedPipeSignal:PipeNamePrefix"] = prefix,
            ["MsgHatch:NamedPipeSignal:PipeNamePrefix"] = "from-default-root",
        });
        services.AddMsgHatch(configSectionPath: "Messaging").UseNamedPipeDispatchSignal();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<NamedPipeSignalOptions>>().Value;

        // Assert
        Assert.Equal(prefix, options.PipeNamePrefix);
    }

    [Fact]
    public async Task UseNamedPipeDispatchSignal_PrefixInTheSection_ReachesTheOptions()
    {
        // Arrange
        const string prefix = "from-section";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:NamedPipeSignal:PipeNamePrefix"] = prefix });
        services.AddMsgHatch().UseNamedPipeDispatchSignal();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<NamedPipeSignalOptions>>().Value;

        // Assert
        Assert.Equal(prefix, options.PipeNamePrefix);
    }

    [Fact]
    public async Task UseNamedPipeDispatchSignal_PrefixFromCode_WinsOverTheSection()
    {
        // Arrange
        const string prefix = "from-code";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:NamedPipeSignal:PipeNamePrefix"] = "from-section" });
        services.AddMsgHatch().UseNamedPipeDispatchSignal(static o => o.PipeNamePrefix = prefix);
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<NamedPipeSignalOptions>>().Value;

        // Assert
        Assert.Equal(prefix, options.PipeNamePrefix);
    }

    [Fact]
    public async Task Validate_CompleteSetupWithPolling_Succeeds()
    {
        // Arrange
        await using var provider = Build(static _ => { });

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
        await using var provider = Build(builder => builder.Services.Configure<DispatchOptions>(dispatcher, static o => o.EnablePolling = false));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal((typeof(DispatchOptions), dispatcher), (failure.OptionsType, failure.OptionsName));
        Assert.StartsWith("UseNamedPipeDispatchSignal needs EnablePolling ", Assert.Single(failure.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_AfterUseDispatchSignal_NamesBothSetupMethods()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores().UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .UseDispatchSignal(static _ => new InProcessDispatchSignal())
            .UseNamedPipeDispatchSignal(static o => o.PipeNamePrefix = _prefix);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.StartsWith("UseDispatchSignal and UseNamedPipeDispatchSignal select ", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_ChannelNameThatMakesThePipePathTooLong_ThrowsOptionsValidationExceptionThatNamesTheTopic()
    {
        // Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Only Linux and macOS make a pipe a Unix domain socket with a path limit.");
        var longChannel = new ChannelId(new string('A', 100));
        await using var provider = Build(builder => builder
            .AddChannelType(
                new ChannelDescriptor<TestChannelSettings>(longChannel, "Long channel", ChannelFeatures.None), 1,
                TestJsonContext.Default.TestChannelSettings, Substitute.For<IChannelSettingsValidator>())
            .UseTransport(_ => TestTransports.For(longChannel)));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal(typeof(NamedPipeSignalOptions), failure.OptionsType);
        Assert.Contains(DispatchTopic.ForChannel(longChannel).Name, Assert.Single(failure.Failures), StringComparison.Ordinal);
    }

    private static ServiceProvider Build(Action<MsgHatchBuilder> more)
    {
        var services = TestHost.CreateServices();
        var builder = services.AddMsgHatch().AddTestChannel().UseInMemoryStores().UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .UseNamedPipeDispatchSignal(static o => o.PipeNamePrefix = _prefix);
        more(builder);
        return services.Build();
    }
}
