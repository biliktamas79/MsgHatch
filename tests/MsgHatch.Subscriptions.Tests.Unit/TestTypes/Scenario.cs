using MsgHatch.Broker;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.Resolution;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>
/// The shared test scenario "Acme and Globex" of section 4 of <c>docs/subscription-domain.md</c>, with no tenant. Each
/// subscription has a fixed id, so that a test can name it by its label, for example <c>#6</c>.
/// </summary>
public static class Scenario
{
    public static readonly CompanyId Acme = new("100");
    public static readonly CompanyId Globex = new("200");

    public static readonly IReadOnlyList<CompanyProfile> Companies =
    [
        new(Acme, "hu", new Dictionary<ChannelId, string> { [EmailChannel.Id] = "admin@acme.example" }),
        new(Globex, "en", new Dictionary<ChannelId, string>()),
    ];

    public static readonly IReadOnlyList<UserProfile> Users =
    [
        User("1", Acme, "en", "client1@acme.example"),
        User("2", Acme, "hu", "client2@acme.example"),
        User("3", Globex, "en", "client3@globex.example"),
        User("4", company: null, "de", "client4@example.com"),
        User("5", Acme, "hu", email: null),
    ];

    public static readonly Subscription S1 = Default(1, "A", EmailChannel.Id, new EmailChannelSettings());
    public static readonly Subscription S2 = Default(2, "B", EmailChannel.Id, new EmailChannelSettings());
    public static readonly Subscription S3 = Default(3, "C", SignalRChannel.Id, new SignalRChannelSettings()) with { Mandatory = true };
    public static readonly Subscription S4 = OptOut(4, SubscriptionOwner.ForCompany(Acme), "B");
    public static readonly Subscription S5 = Of(5, SubscriptionOwner.ForCompany(Acme), "E", EmailChannel.Id, new EmailChannelSettings("en"));
    public static readonly Subscription S6 =
        Of(6, SubscriptionOwner.ForUser(new UserId("1")), "B", EmailChannel.Id, new EmailChannelSettings("en", EmailFormat.PlainText));

    public static readonly Subscription S7 = OptOut(7, SubscriptionOwner.ForUser(new UserId("1")), "E");
    public static readonly Subscription S8 = Of(8, SubscriptionOwner.ForCompany(Globex), "D", SignalRChannel.Id, new SignalRChannelSettings());
    public static readonly Subscription S9 = Of(9, SubscriptionOwner.ForCompany(Globex), "D", EmailChannel.Id, new EmailChannelSettings("en"));
    public static readonly Subscription S10 =
        Of(10, SubscriptionOwner.ForCompany(Acme), "B", EmailChannel.Id, new EmailChannelSettings()) with { Recipients = SubscriptionRecipients.Company };

    public static readonly Subscription S11 =
        Of(11, SubscriptionOwner.ForCompany(Acme), "D", EmailChannel.Id, new EmailChannelSettings())
            with { Recipients = SubscriptionRecipients.ForUsers([new UserId("2")]) };

    public static readonly Subscription S12 = Of(12, SubscriptionOwner.ForUser(new UserId("2")), "D", EmailChannel.Id, new EmailChannelSettings("en"));

    public static readonly IReadOnlyList<Subscription> Subscriptions = [S1, S2, S3, S4, S5, S6, S7, S8, S9, S10, S11, S12];

    /// <summary>The registered channels of each message type A to E.</summary>
    public static IReadOnlyList<ChannelDescriptor> ChannelsOf(string type) => type switch
    {
        "A" or "B" => [EmailChannel.Descriptor],
        "C" => [EmailChannel.Descriptor, SignalRChannel.Descriptor],
        "D" => [EmailChannel.Descriptor, SignalRChannel.Descriptor, BrokerChannel.Descriptor],
        _ => [EmailChannel.Descriptor, BrokerChannel.Descriptor],
    };

    /// <summary>The subscriptions of one message type, as <c>FindForDeliveryAsync</c> gives them with all owners.</summary>
    public static IReadOnlyList<Subscription> SubscriptionsOf(string type, IReadOnlyList<Subscription>? subscriptions = null) =>
        [.. (subscriptions ?? Subscriptions).Where(subscription => subscription.Type.Name == type)];

    /// <summary>Gives the scope of a text <c>"users 1,2"</c> or <c>"company 100"</c>.</summary>
    public static NotificationScope ScopeOf(string scope) =>
        scope.StartsWith("company ", StringComparison.Ordinal)
            ? NotificationScope.ForCompany(new CompanyId(scope["company ".Length..]))
            : NotificationScope.ForUsers(scope["users ".Length..].Split(','));

    /// <summary>Gives a delivery in the form of the examples: <c>u:1 / Email / en / Default</c>.</summary>
    public static string Describe(MessageRoute route) =>
        $"{(route.Target.Kind == DeliveryTargetKind.User ? "u" : "c")}:{route.Target.Id} / {route.Channel.Name} / {route.Language} / "
        + ((ResolutionTrace)route.Trace!).Applied.Owner.Level;

    /// <summary>Gives the label of a subscription of this scenario, or of a subscription with a <see cref="LabelId"/> id.</summary>
    public static string Label(Guid id) => $"#{id.ToByteArray()[15]}";

    /// <summary>A fixed id whose label is <c>#{label}</c>.</summary>
    public static Guid LabelId(int label)
    {
        var bytes = new byte[16];
        bytes[0] = 0xAC;
        bytes[15] = (byte)label;
        return new Guid(bytes);
    }

    private static UserProfile User(string id, CompanyId? company, string uiLanguage, string? email) =>
        new(
            new UserId(id), company, uiLanguage,
            email is null ? new Dictionary<ChannelId, string>() : new Dictionary<ChannelId, string> { [EmailChannel.Id] = email });

    private static Subscription Default(int label, string type, ChannelId channel, ChannelSettings settings) =>
        Of(label, SubscriptionOwner.Default, type, channel, settings);

    private static Subscription Of(int label, SubscriptionOwner owner, string type, ChannelId channel, ChannelSettings settings) =>
        new(
            LabelId(label), 1, owner, new MessageType(type), SubscriptionKind.Channel, channel, settings, SubscriptionRecipients.ScopeUsers,
            Mandatory: false, TestSubscriptions.CreatedAtUtc, TestSubscriptions.CreatedAtUtc);

    private static Subscription OptOut(int label, SubscriptionOwner owner, string type) =>
        new(
            LabelId(label), 1, owner, new MessageType(type), SubscriptionKind.OptOut, Channel: null, Settings: null,
            SubscriptionRecipients.ScopeUsers, Mandatory: false, TestSubscriptions.CreatedAtUtc, TestSubscriptions.CreatedAtUtc);
}
