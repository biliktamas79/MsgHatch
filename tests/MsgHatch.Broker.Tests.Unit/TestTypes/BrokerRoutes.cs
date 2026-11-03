using MsgHatch.Domain.Routing;

namespace MsgHatch.Broker.Tests.Unit.TestTypes;

/// <summary>Shared by the broker tests: valid broker routes to a company and to a user.</summary>
public static class BrokerRoutes
{
    public static MessageRoute ACompanyRoute(string companyId = "c1", IReadOnlyDictionary<string, string>? headers = null) =>
        new(new DeliveryTarget(DeliveryTargetKind.Company, companyId), BrokerChannel.Id, new BrokerChannelSettings(headers), "en", Address: null, TemplateScope: null, Trace: null);

    public static MessageRoute AUserRoute(string userId = "u1", IReadOnlyDictionary<string, string>? headers = null) =>
        new(new DeliveryTarget(DeliveryTargetKind.User, userId), BrokerChannel.Id, new BrokerChannelSettings(headers), "en", Address: null, TemplateScope: null, Trace: null);
}
