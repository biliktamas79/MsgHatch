using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

public sealed class ChannelDownOptionsValidatorUnitTests
{
    private const string _errorThreshold = nameof(ChannelDownOptions.ErrorThreshold);
    private const string _checkInterval = nameof(ChannelDownOptions.CheckInterval);

    public static TheoryData<int, string[]> ErrorThresholdRows => new()
    {
        { -1, [_errorThreshold] },
        { 0, [_errorThreshold] },
        { 1, [] },
        { 1000, [] },
        { 1001, [_errorThreshold] },
    };

    public static TheoryData<TimeSpan, string[]> CheckIntervalRows => new()
    {
        { TimeSpan.FromMilliseconds(999), [_checkInterval] },
        { TimeSpan.FromSeconds(1), [] },
        { TimeSpan.FromHours(1), [] },
        { TimeSpan.Parse("01:00:00.001", CultureInfo.InvariantCulture), [_checkInterval] },
    };

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        // Act
        var result = new ChannelDownOptionsValidator().Validate("Test", new ChannelDownOptions());

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(ErrorThresholdRows))]
    public void Validate_ErrorThreshold_FailsOutsideTheRange(int value, string[] expectedFailedProperties)
    {
        // Act
        var result = new ChannelDownOptionsValidator().Validate("Test", new ChannelDownOptions { ErrorThreshold = value });

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(CheckIntervalRows))]
    public void Validate_CheckInterval_FailsOutsideTheRange(TimeSpan value, string[] expectedFailedProperties)
    {
        // Act
        var result = new ChannelDownOptionsValidator().Validate("Test", new ChannelDownOptions { CheckInterval = value });

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Fact]
    public async Task AddChannelType_SectionOfTheChannel_BindsTheOptionsOfThatChannelOnly()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:ChannelDown:Test:ErrorThreshold"] = "2" });
        services.AddMsgHatch().AddTestChannel().AddSecondTestChannel();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptionsMonitor<ChannelDownOptions>>();

        // Assert
        Assert.Equal([2, 5], [options.Get("Test").ErrorThreshold, options.Get("Second").ErrorThreshold]);
    }

    [Fact]
    public async Task ConfigureChannelDown_BeforeTheChannelTypeAndWithAValueInTheSection_TheValueInCodeWins()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:ChannelDown:Test:ErrorThreshold"] = "2" });
        services.AddMsgHatch().ConfigureChannelDown(TestChannel.Id, static o => o.ErrorThreshold = 3).AddTestChannel();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptionsMonitor<ChannelDownOptions>>().Get("Test");

        // Assert
        Assert.Equal(3, options.ErrorThreshold);
    }

    [Fact]
    public async Task ConfigureChannelDown_ErrorThresholdZero_FailsTheStartupValidation()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores().UseTransport(static _ => TestTransports.For(TestChannel.Id)).ConfigureChannelDown(TestChannel.Id, static o => o.ErrorThreshold = 0);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }
}
