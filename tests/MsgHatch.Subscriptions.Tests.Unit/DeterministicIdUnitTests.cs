using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Tests.Unit;

public sealed class DeterministicIdUnitTests
{
    [Fact]
    public void Create_UserAndCompanyTargetsWithTheSameIdOnSignalR_GiveTwoDifferentIds_V4()
    {
        // Arrange
        var messageId = Guid.Parse("0b6c1f7e-2a44-4d8e-9a3b-61c0f5e2d7a9");
        var user = new DeliveryTarget(DeliveryTargetKind.User, "1");
        var company = new DeliveryTarget(DeliveryTargetKind.Company, "1");

        // Act
        Guid[] ids =
        [
            DeterministicId.Create(messageId, null, user, new ChannelId("SignalR")),
            DeterministicId.Create(messageId, null, company, new ChannelId("SignalR")),
        ];

        // Assert
        Assert.Equal((false, false), (user == company, ids[0] == ids[1]));
    }
}
