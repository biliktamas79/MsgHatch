using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Broker.Tests.Unit;

/// <summary>The settings validator of the broker channel (section 4.4): the header names that the library reserves.</summary>
public sealed class BrokerChannelSettingsValidatorUnitTests
{
    private const ChannelFeatures _brokerFeatures = ChannelFeatures.RawData | ChannelFeatures.GroupDelivery;

    public static TheoryData<string, ChannelSettings, int?> SettingsRows => new()
    {
        { "defaults", new BrokerChannelSettings(), null },
        { "the header x-source", Settings("x-source"), null },
        { "a header name with accented characters", Settings("x-forrás"), null },
        { "an empty header name", Settings(""), 400 },
        { "a header name of only whitespace", Settings(" "), 400 },
        { "the header msghatch-x", Settings("msghatch-x"), 400 },
        { "the header MsgHatch-X", Settings("MsgHatch-X"), 400 },
        { "the settings of another channel", new OtherTestChannelSettings(), 400 },
    };

    [Fact]
    public void Channel_Validator_IsForTheBrokerChannel()
    {
        // Arrange
        var validator = new BrokerChannelSettingsValidator();

        // Act
        var channel = validator.Channel;

        // Assert
        Assert.Equal(BrokerChannel.Id, channel);
    }

    [Theory]
    [MemberData(nameof(SettingsRows))]
    public void Validate_Settings_GivesTheErrorCodeOfTheRule(string row, ChannelSettings settings, int? expectedCode)
    {
        // Arrange
        var validator = new BrokerChannelSettingsValidator();

        // Act
        var error = validator.Validate(settings, _brokerFeatures);

        // Assert
        Assert.Equal((row, expectedCode), (row, error?.Type.Code));
    }

    private static BrokerChannelSettings Settings(string headerName) =>
        new(new Dictionary<string, string> { ["x-other"] = "1", [headerName] = "value" });
}
