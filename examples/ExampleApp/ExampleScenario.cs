using MsgHatch.Domain.Identity;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions;

namespace ExampleApp;

/// <summary>
/// The shared test scenario "Acme and Globex" of section 4 of <c>docs/subscription-domain.md</c>: its companies, its
/// users, and its 12 subscriptions. The app writes it at its first start (D157), and the tests seed it too.
/// </summary>
public static class ExampleScenario
{
    /// <summary>The id of the company Acme.</summary>
    public const int Acme = 100;

    /// <summary>The id of the company Globex.</summary>
    public const int Globex = 200;

    /// <summary>The companies Acme and Globex.</summary>
    public static IReadOnlyList<ExampleCompany> Companies { get; } =
    [
        new(Acme, "hu", "admin@acme.example"),
        new(Globex, "en", Email: null),
    ];

    /// <summary>The users Client 1 to Client 5.</summary>
    public static IReadOnlyList<ExampleUser> Users { get; } =
    [
        new(1, Acme, "en", "client1@acme.example"),
        new(2, Acme, "hu", "client2@acme.example"),
        new(3, Globex, "en", "client3@globex.example"),
        new(4, CompanyId: null, "de", "client4@example.com"),
        new(5, Acme, "hu", Email: null),
    ];

    /// <summary>The subscriptions #1 to #12, in the order of their labels.</summary>
    public static IReadOnlyList<SubscriptionDraft> Subscriptions { get; } =
    [
        Draft(SubscriptionOwner.Default, "A", EmailChannel.Id, new EmailChannelSettings()),
        Draft(SubscriptionOwner.Default, "B", EmailChannel.Id, new EmailChannelSettings()),
        Draft(SubscriptionOwner.Default, "C", SignalRChannel.Id, new SignalRChannelSettings()) with { Mandatory = true },
        OptOut(Company(Acme), "B"),
        Draft(Company(Acme), "E", EmailChannel.Id, new EmailChannelSettings("en")),
        Draft(User(1), "B", EmailChannel.Id, new EmailChannelSettings("en", EmailFormat.PlainText)),
        OptOut(User(1), "E"),
        Draft(Company(Globex), "D", SignalRChannel.Id, new SignalRChannelSettings()),
        Draft(Company(Globex), "D", EmailChannel.Id, new EmailChannelSettings("en")),
        Draft(Company(Acme), "B", EmailChannel.Id, new EmailChannelSettings()) with { Recipients = SubscriptionRecipients.Company },
        Draft(Company(Acme), "D", EmailChannel.Id, new EmailChannelSettings()) with
        {
            Recipients = SubscriptionRecipients.ForUsers([UserId.From(2)]),
        },
        Draft(User(2), "D", EmailChannel.Id, new EmailChannelSettings("en")),
    ];

    private static SubscriptionOwner Company(int id) => SubscriptionOwner.ForCompany(CompanyId.From(id));

    private static SubscriptionOwner User(int id) => SubscriptionOwner.ForUser(UserId.From(id));

    private static SubscriptionDraft Draft(SubscriptionOwner owner, string type, ChannelId channel, MsgHatch.Domain.Routing.ChannelSettings settings) =>
        new(owner, new MessageType(type), SubscriptionKind.Channel, channel, settings, SubscriptionRecipients.ScopeUsers);

    private static SubscriptionDraft OptOut(SubscriptionOwner owner, string type) =>
        new(owner, new MessageType(type), SubscriptionKind.OptOut, Channel: null, Settings: null, SubscriptionRecipients.ScopeUsers);
}
