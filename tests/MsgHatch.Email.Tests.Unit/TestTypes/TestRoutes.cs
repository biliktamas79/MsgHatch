using MsgHatch.Domain.Routing;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>Shared by the email tests: a valid email route, with named parameters for the values that tests vary.</summary>
public static class TestRoutes
{
    public static MessageRoute AnEmailRoute(
        string userId = "u1",
        string language = "en",
        string? address = "client1@example.com",
        string? templateScope = null,
        RouteTrace? trace = null,
        EmailFormat format = EmailFormat.Html) =>
        new(
            new DeliveryTarget(DeliveryTargetKind.User, userId), EmailChannel.Id, new EmailChannelSettings(Format: format),
            language, address, templateScope, trace);
}
