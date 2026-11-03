using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.SignalR.Tests.Unit;

public sealed class SignalRChannelUnitTests
{
    [Fact]
    public void Descriptor_SignalRChannel_HasTheIdTheLanguagesRawDataAndGroupDeliveryAndTheSignalRSettings()
    {
        // Arrange
        var descriptor = SignalRChannel.Descriptor;

        // Act
        var values = (descriptor.Id, descriptor.Features, descriptor.SettingsType);

        // Assert
        Assert.Equal(
            (new ChannelId("SignalR"), ChannelFeatures.Languages | ChannelFeatures.RawData | ChannelFeatures.GroupDelivery, typeof(SignalRChannelSettings)),
            values);
    }

    [Fact]
    public void SignalRChannelSettings_Defaults_HaveNoLanguage()
    {
        // Arrange
        var settings = new SignalRChannelSettings();

        // Act
        var language = settings.Language;

        // Assert
        Assert.Null(language);
    }
}
