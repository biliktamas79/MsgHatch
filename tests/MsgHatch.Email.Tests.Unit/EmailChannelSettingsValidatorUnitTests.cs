using MsgHatch.Domain.Routing;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Tests.Unit;

public sealed class EmailChannelSettingsValidatorUnitTests
{
    private const ChannelFeatures _emailFeatures = ChannelFeatures.Languages | ChannelFeatures.RichText;

    public static TheoryData<string, ChannelSettings, ChannelFeatures, int?> SettingsRows => new()
    {
        { "defaults", new EmailChannelSettings(), _emailFeatures, null },
        { "a language", new EmailChannelSettings("hu-HU"), _emailFeatures, null },
        { "plain text with rich text", new EmailChannelSettings(Format: EmailFormat.PlainText), _emailFeatures, null },
        { "plain text with no rich text", new EmailChannelSettings(Format: EmailFormat.PlainText), ChannelFeatures.Languages, null },
        { "html with no rich text", new EmailChannelSettings(Format: EmailFormat.Html), ChannelFeatures.Languages, 400 },
        { "an unknown format", new EmailChannelSettings(Format: (EmailFormat)7), _emailFeatures, 400 },
        { "the settings of another channel", new OtherChannelSettings(), _emailFeatures, 400 },
    };

    [Fact]
    public void Channel_Validator_IsForTheEmailChannel()
    {
        // Arrange
        var validator = new EmailChannelSettingsValidator();

        // Act
        var channel = validator.Channel;

        // Assert
        Assert.Equal(EmailChannel.Id, channel);
    }

    [Theory]
    [MemberData(nameof(SettingsRows))]
    public void Validate_Settings_GivesTheErrorCodeOfTheRule(string row, ChannelSettings settings, ChannelFeatures features, int? expectedCode)
    {
        // Arrange
        var validator = new EmailChannelSettingsValidator();

        // Act
        var error = validator.Validate(settings, features);

        // Assert
        Assert.Equal((row, expectedCode), (row, error?.Type.Code));
    }
}
