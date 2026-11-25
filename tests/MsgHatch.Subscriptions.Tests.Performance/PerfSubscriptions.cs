using MsgHatch.Channels.Email;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Tests.Performance;

/// <summary>
/// The data of the subscription benchmarks: the company Acme with its users, and the subscriptions of the message type
/// A: an email default for the scope users, a company opt-out, and a user subscription in English.
/// </summary>
public static class PerfSubscriptions
{
    public static readonly MessageType Type = new("A");
    public static readonly CompanyId Acme = new("100");

    private static readonly DateTime _createdAtUtc = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<Subscription> Subscriptions { get; } =
    [
        Of(SubscriptionOwner.Default, SubscriptionKind.Channel, new EmailChannelSettings()),
        Of(SubscriptionOwner.ForCompany(Acme), SubscriptionKind.OptOut, settings: null),
        Of(SubscriptionOwner.ForUser(new UserId("1")), SubscriptionKind.Channel, new EmailChannelSettings("en")),
    ];

    public static IReadOnlyList<CompanyProfile> Companies { get; } =
        [new(Acme, "hu", new Dictionary<ChannelId, string> { [EmailChannel.Id] = "admin@acme.example" })];

    /// <summary>The users 1 to <paramref name="count"/> of Acme, each with a UI language and an email address.</summary>
    public static UserProfile[] UsersOf(int count) =>
        [.. Enumerable.Range(1, count).Select(static user => new UserProfile(
            new UserId($"{user}"), Acme, user % 2 == 0 ? "hu" : "en",
            new Dictionary<ChannelId, string> { [EmailChannel.Id] = $"client{user}@acme.example" }))];

    private static Subscription Of(SubscriptionOwner owner, SubscriptionKind kind, EmailChannelSettings? settings) =>
        new(
            Guid.NewGuid(), 1, owner, Type, kind, kind == SubscriptionKind.Channel ? EmailChannel.Id : null, settings,
            SubscriptionRecipients.ScopeUsers, Mandatory: false, _createdAtUtc, _createdAtUtc);
}
