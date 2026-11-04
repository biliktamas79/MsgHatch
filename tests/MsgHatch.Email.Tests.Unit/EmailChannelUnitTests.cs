using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Email.Tests.Unit;

public sealed class EmailChannelUnitTests
{
    [Fact]
    public void Descriptor_EmailChannel_HasTheIdTheLanguagesAndRichTextAndTheEmailSettings()
    {
        // Arrange
        var descriptor = EmailChannel.Descriptor;

        // Act
        var values = (descriptor.Id, descriptor.Features, descriptor.SettingsType);

        // Assert
        Assert.Equal((new ChannelId("Email"), ChannelFeatures.Languages | ChannelFeatures.RichText, typeof(EmailChannelSettings)), values);
    }

    [Fact]
    public void EmailChannelSettings_Defaults_HaveNoLanguageAndTheHtmlFormat()
    {
        // Arrange
        var settings = new EmailChannelSettings();

        // Act
        var values = (settings.Language, settings.Format);

        // Assert
        Assert.Equal(((string?)null, EmailFormat.Html), values);
    }

    [Fact]
    public void EmailChannelSettings_LanguageHu_GivesHuThroughILanguageSettings()
    {
        // Arrange
        ChannelSettings settings = new EmailChannelSettings("hu");

        // Act
        var language = Assert.IsAssignableFrom<ILanguageSettings>(settings).Language;

        // Assert
        Assert.Equal("hu", language);
    }
}
