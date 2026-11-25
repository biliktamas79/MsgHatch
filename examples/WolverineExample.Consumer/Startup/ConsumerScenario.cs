using MsgHatch.Channels.Email;
using MsgHatch.Channels.SignalR;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions;
using WolverineExample.Contracts;

namespace WolverineExample.Consumer.Startup;

/// <summary>
/// The example data of the consumer: two tenants with their companies and users, and the same default subscriptions in
/// each tenant. The tenant <c>south</c> has a company <c>100</c> and users <c>1</c> and <c>2</c> too, so the example
/// shows that the tenants stay apart (D114).
/// </summary>
public static class ConsumerScenario
{
    /// <summary>The tenant of the sample events of the publisher.</summary>
    public const string North = "north";

    /// <summary>The second tenant, with the same ids as <see cref="North"/>.</summary>
    public const string South = "south";

    /// <summary>The company Acme of the tenant <see cref="North"/>, with the users 1 and 2, and a Hungarian administrator.</summary>
    public const int Acme = 100;

    /// <summary>The company Globex of the tenant <see cref="North"/>, with the users 3 and 4, and an English administrator.</summary>
    public const int Globex = 200;

    /// <summary>The company Initech of the tenant <see cref="South"/>, with the users 1 and 2, and an English administrator.</summary>
    public const int Initech = 100;

    /// <summary>The tenants, each with its companies and users.</summary>
    public static IReadOnlyList<ScenarioTenant> Tenants { get; } =
    [
        new(
            North,
            [new(Acme, "hu", "admin@acme.example"), new(Globex, "en", "admin@globex.example")],
            [new(1, Acme), new(2, Acme), new(3, Globex), new(4, Globex)]),
        new(South, [new(Initech, "en", "admin@initech.example")], [new(1, Initech), new(2, Initech)]),
    ];

    /// <summary>
    /// The default subscriptions of each tenant: for each message type, a SignalR notification to the scope users and an
    /// email to the company. For a user scope, the scope user gets the SignalR notification, and the company of that user
    /// gets the email. For a company scope, the SignalR group of the company gets the notification, and the company gets
    /// the email. The company address of the email channel is the email address of the company administrator.
    /// </summary>
    public static IReadOnlyList<SubscriptionDraft> Subscriptions { get; } = [.. DomainEvents.Names.SelectMany(SignalRAndAdminEmail)];

    private static SubscriptionDraft[] SignalRAndAdminEmail(string type) =>
    [
        new(SubscriptionOwner.Default, new MessageType(type), SubscriptionKind.Channel, SignalRChannel.Id, new SignalRChannelSettings(), SubscriptionRecipients.ScopeUsers),
        new(SubscriptionOwner.Default, new MessageType(type), SubscriptionKind.Channel, EmailChannel.Id, new EmailChannelSettings(), SubscriptionRecipients.Company),
    ];
}
