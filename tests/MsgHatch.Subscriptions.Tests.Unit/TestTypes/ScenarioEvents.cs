using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>The scope function of the test events: a user scope, or a company scope when the event has a company.</summary>
internal static class ScenarioEvents
{
    public static NotificationScope ScopeOf(string[]? users, string? company) =>
        company is null ? NotificationScope.ForUsers(users ?? []) : NotificationScope.ForCompany(new CompanyId(company));
}

/// <summary>The domain event of the message type A of the scenario: Email.</summary>
internal sealed record EventA(string[]? Users = null, string? Company = null);

/// <summary>The domain event of the message type B of the scenario: Email.</summary>
internal sealed record EventB(string[]? Users = null, string? Company = null);

/// <summary>The domain event of the message type C of the scenario: Email and SignalR.</summary>
internal sealed record EventC(string[]? Users = null, string? Company = null);

/// <summary>The domain event of the message type D of the scenario: Email, SignalR, and the broker.</summary>
internal sealed record EventD(string[]? Users = null, string? Company = null);

/// <summary>The domain event of the message type E of the scenario: Email and the broker.</summary>
internal sealed record EventE(string[]? Users = null, string? Company = null);

/// <summary>The domain event of the test message type G, with only the group channel SignalR.</summary>
internal sealed record EventG(string[]? Users = null, string? Company = null);
