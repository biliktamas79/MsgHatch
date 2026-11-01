using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>Shared by the grouping and pipeline tests: a valid route on the test channel, with named parameters for the values that tests vary.</summary>
public static class TestRoutes
{
    public static MessageRoute ARoute(
        string userId = "u1",
        ChannelId? channel = null,
        ChannelSettings? settings = null,
        string language = "en",
        string? address = null,
        string? templateScope = null,
        RouteTrace? trace = null) =>
        new(
            new DeliveryTarget(DeliveryTargetKind.User, userId), channel ?? TestChannel.Id, settings ?? new TestChannelSettings(),
            language, address, templateScope, trace);
}
