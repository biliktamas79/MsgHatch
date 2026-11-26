using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

public sealed class ServiceCollectionExtensionsUnitTests
{
    private static readonly CoreValues _configuredValues =
        new("de", 11, TimeSpan.FromSeconds(2), 3, 3, 4, 7, TestDeliveryMode.Deliver, TimeSpan.FromMinutes(1));

    private static readonly CoreValues _defaultValues =
        new("en", 8, TimeSpan.FromSeconds(5), 8, 8, 8, 5, TestDeliveryMode.Suppress, TimeSpan.FromSeconds(30));

    public static TheoryData<string?, string> RootRows => new()
    {
        { null, "MsgHatch" },
        { "Messaging", "Messaging" },
        { "App:Messaging", "App:Messaging" },
    };

    [Theory]
    [MemberData(nameof(RootRows))]
    public async Task AddMsgHatch_ConfigSectionPath_BindsTheCoreOptionsUnderTheRoot(string? configSectionPath, string root)
    {
        // Arrange
        await using var provider = BuildWithCoreSettings(configSectionPath, CoreSettingsUnder(root));

        // Act
        var values = CoreValuesOf(provider);

        // Assert
        Assert.Equal(_configuredValues, values);
    }

    [Fact]
    public async Task AddMsgHatch_ConfigSectionPath_IgnoresTheValuesUnderTheDefaultRoot()
    {
        // Arrange
        await using var provider = BuildWithCoreSettings("Messaging", CoreSettingsUnder(MsgHatchOptions.DefaultSectionKey));

        // Act
        var values = CoreValuesOf(provider);

        // Assert
        Assert.Equal(_defaultValues, values);
    }

    [Fact]
    public async Task AddMsgHatch_SecondCallWithNoRoot_KeepsTheRootOfTheFirstCall()
    {
        // Arrange
        const string root = "Messaging";
        const string language = "de";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [$"{root}:DefaultLanguage"] = language });
        services.AddMsgHatch(static options => options.Roles = MsgHatchRoles.None, root);
        var builder = services.AddMsgHatch();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            (root, language, (Exception?)null),
            (builder.ConfigSectionPath, provider.GetRequiredService<IOptions<MsgHatchOptions>>().Value.DefaultLanguage, exception));
    }

    [Fact]
    public async Task AddMsgHatch_SecondCallWithAnotherRoot_FailsTheStartupValidation()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static options => options.Roles = MsgHatchRoles.None);
        services.AddMsgHatch(configSectionPath: "Messaging");
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddMsgHatch_EmptyOrWhiteSpaceRoot_ThrowsArgumentException(string configSectionPath)
    {
        // Arrange
        var services = TestHost.CreateServices();

        // Act
        var exception = Record.Exception(() => services.AddMsgHatch(configSectionPath: configSectionPath));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public async Task AddMsgHatch_SecondCall_KeepsTheRegistrationsOfTheFirstCall()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel();
        services.AddMsgHatch().AddSecondTestChannel();

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.Equal([TestChannel.Descriptor, TestChannel.SecondDescriptor], provider.GetRequiredService<IMessageRegistry>().Channels);
    }

    [Fact]
    public async Task AddMsgHatch_PlainCallAfterACallWithACodeValue_KeepsTheCodeValue()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:DefaultLanguage"] = "de" });
        services.AddMsgHatch(static options => options.DefaultLanguage = "hu");
        services.AddMsgHatch();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<MsgHatchOptions>>().Value;

        // Assert
        Assert.Equal("hu", options.DefaultLanguage);
    }

    [Fact]
    public async Task AddMsgHatch_NoSignalSelection_RegistersTheInProcessDispatchSignalAsASingleton()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch();
        await using var provider = services.Build();

        // Act
        var signal = provider.GetRequiredService<IDispatchSignal>();

        // Assert
        Assert.IsType<InProcessDispatchSignal>(signal);
        Assert.Same(signal, provider.GetRequiredService<IDispatchSignal>());
    }

    [Fact]
    public async Task UseDispatchSignal_SignalType_ReplacesTheInProcessDispatchSignal()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseDispatchSignal<OtherDispatchSignal>();
        await using var provider = services.Build();

        // Act
        var signal = provider.GetRequiredService<IDispatchSignal>();

        // Assert
        Assert.IsType<OtherDispatchSignal>(signal);
    }

    [Fact]
    public async Task AddMsgHatch_ExpiryInTheInboxRetrySection_FailsTheValidationOfTheInboxRetryOptionsOnExpiry()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:Retry:Inbox:Expiry"] = "00:05:00" });
        services.AddMsgHatch();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(() => provider.GetRequiredService<IOptionsMonitor<RetryOptions>>().Get("Inbox"));

        // Assert
        var validation = Assert.IsType<OptionsValidationException>(exception);
        var failure = Assert.Single(validation.Failures);
        Assert.Equal(("Inbox", "Expiry"), (validation.OptionsName, failure[..failure.IndexOf(':', StringComparison.Ordinal)]));
    }

    private static Dictionary<string, string?> CoreSettingsUnder(string root) => new()
    {
        [$"{root}:DefaultLanguage"] = "de",
        [$"{root}:Retry:Inbox:MaxTryCount"] = "11",
        [$"{root}:Retry:Test:BaseDelay"] = "00:00:02",
        [$"{root}:Retry:Test:TestEnvelope:MaxTryCount"] = "3",
        [$"{root}:Dispatch:Inbox:MaxDegreeOfParallelism"] = "3",
        [$"{root}:Dispatch:Outbox:MaxDegreeOfParallelism"] = "4",
        [$"{root}:ChannelDown:Test:ErrorThreshold"] = "7",
        [$"{root}:TestDelivery:Test:Mode"] = "Deliver",
        [$"{root}:Registry:RefreshInterval"] = "00:01:00",
    };

    private static ServiceProvider BuildWithCoreSettings(string? configSectionPath, Dictionary<string, string?> configuration)
    {
        var services = TestHost.CreateServices(configuration: configuration);
        services.AddMsgHatch(configSectionPath: configSectionPath)
            .AddTestChannel()
            .UseInMemoryStores()
            .UseStoredRegistry()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()));
        return services.Build();
    }

    private static CoreValues CoreValuesOf(IServiceProvider provider)
    {
        var retry = provider.GetRequiredService<IOptionsMonitor<RetryOptions>>();
        var dispatch = provider.GetRequiredService<IOptionsMonitor<DispatchOptions>>();
        return new CoreValues(
            provider.GetRequiredService<IOptions<MsgHatchOptions>>().Value.DefaultLanguage,
            retry.Get("Inbox").MaxTryCount,
            retry.Get("Test:TestEnvelope").BaseDelay,
            retry.Get("Test:TestEnvelope").MaxTryCount,
            dispatch.Get("Inbox").MaxDegreeOfParallelism,
            dispatch.Get("Outbox").MaxDegreeOfParallelism,
            provider.GetRequiredService<IOptionsMonitor<ChannelDownOptions>>().Get("Test").ErrorThreshold,
            provider.GetRequiredService<IOptionsMonitor<TestDeliveryOptions>>().Get("Test").Mode,
            provider.GetRequiredService<IOptions<RegistryOptions>>().Value.RefreshInterval);
    }

    private sealed record CoreValues(
        string DefaultLanguage,
        int InboxMaxTryCount,
        TimeSpan ChannelBaseDelay,
        int ContentTypeMaxTryCount,
        int InboxMaxDegreeOfParallelism,
        int OutboxMaxDegreeOfParallelism,
        int ChannelErrorThreshold,
        TestDeliveryMode ChannelTestDeliveryMode,
        TimeSpan RegistryRefreshInterval);

    private sealed class OtherDispatchSignal : IDispatchSignal
    {
        public ValueTask<Error?> NotifyAsync(DispatchTopic topic, CancellationToken ct) => ValueTask.FromResult<Error?>(null);

        public ValueTask<Error?> WaitAsync(DispatchTopic topic, CancellationToken ct) => ValueTask.FromResult<Error?>(null);
    }
}
