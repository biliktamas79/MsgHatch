using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Routing;

public sealed class ChannelDescriptorUnitTests
{
    [Fact]
    public void SettingsType_DescriptorOfASettingsType_IsThatSettingsType()
    {
        // Arrange
        ChannelDescriptor descriptor = new ChannelDescriptor<OtherTestChannelSettings>(new ChannelId("Test"), "Test channel", ChannelFeatures.None);

        // Act
        var settingsType = descriptor.SettingsType;

        // Assert
        Assert.Equal(typeof(OtherTestChannelSettings), settingsType);
    }
}
