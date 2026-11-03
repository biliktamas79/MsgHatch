using MsgHatch.Domain.Routing;

namespace MsgHatch.SignalR.Tests.Unit.TestTypes;

/// <summary>Shared by the SignalR tests: valid SignalR routes to a user and to a company.</summary>
public static class SignalRRoutes
{
    public static MessageRoute AUserRoute(string userId = "u1") =>
        new(new DeliveryTarget(DeliveryTargetKind.User, userId), SignalRChannel.Id, new SignalRChannelSettings(), "en", Address: null, TemplateScope: null, Trace: null);

    public static MessageRoute ACompanyRoute(string companyId = "c1") =>
        new(new DeliveryTarget(DeliveryTargetKind.Company, companyId), SignalRChannel.Id, new SignalRChannelSettings(), "hu", Address: null, TemplateScope: null, Trace: null);
}
