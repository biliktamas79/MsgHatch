using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.SignalR.Tests.Unit;

public sealed class SignalRChannelSettingsValidatorUnitTests
{
    private const ChannelFeatures _signalRFeatures = ChannelFeatures.Languages | ChannelFeatures.RawData | ChannelFeatures.GroupDelivery;

    public static TheoryData<string, ChannelSettings, int?> SettingsRows => new()
    {
        { "defaults", new SignalRChannelSettings(), null },
        { "a language", new SignalRChannelSettings("hu-HU"), null },
        { "an unknown language, which the subscription manager checks", new SignalRChannelSettings("xx-unknown"), null },
        { "the settings of another channel", new OtherTestChannelSettings(), 400 },
    };

    [Fact]
    public void Channel_Validator_IsForTheSignalRChannel()
    {
        // Arrange
        var validator = new SignalRChannelSettingsValidator();

        // Act
        var channel = validator.Channel;

        // Assert
        Assert.Equal(SignalRChannel.Id, channel);
    }

    [Theory]
    [MemberData(nameof(SettingsRows))]
    public void Validate_Settings_GivesTheErrorCodeOfTheRule(string row, ChannelSettings settings, int? expectedCode)
    {
        // Arrange
        var validator = new SignalRChannelSettingsValidator();

        // Act
        var error = validator.Validate(settings, _signalRFeatures);

        // Assert
        Assert.Equal((row, expectedCode), (row, error?.Type.Code));
    }
}
