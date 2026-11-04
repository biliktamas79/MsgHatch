using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Resolution;

/// <summary>The resolution trace examples T1 to T8 of <c>docs/subscription-domain.md</c>. Each row asserts the whole trace.</summary>
public sealed partial class SubscriptionResolverUnitTests
{
    private static readonly IReadOnlyList<Subscription> _t1Subscriptions =
    [
        Scenario.S1 with { Id = Scenario.LabelId(21), Settings = new EmailChannelSettings("de") },
        Scenario.S1 with { Id = Scenario.LabelId(22), Owner = SubscriptionOwner.ForCompany(Scenario.Acme), Settings = new EmailChannelSettings("hu") },
        Scenario.S1 with { Id = Scenario.LabelId(23), Owner = SubscriptionOwner.ForUser(new UserId("1")), Settings = new EmailChannelSettings("en") },
    ];

    private static readonly Subscription _t8User1 =
        Scenario.S3 with { Id = Scenario.LabelId(24), Owner = SubscriptionOwner.ForUser(new UserId("1")), Mandatory = false, Settings = new SignalRChannelSettings("hu") };

    public static TheoryData<string, string, string, string, string> TraceRows => new()
    {
        {
            "T1", "A", "users 1", "u:1 / Email",
            "#23 (User 1) ScopeUsers | ScopeUser | ChannelSettings | UserProfile | skipped: #21 (Default) ScopeUsers Overridden, #22 (Company 100) ScopeUsers Overridden"
        },
        { "T2", "A", "users 1", "u:1 / Email", "#1 (Default) ScopeUsers | ScopeUser | UserUiLanguage | UserProfile | skipped: " },
        {
            "T3", "B", "users 1", "u:1 / Email",
            "#6 (User 1) ScopeUsers | ScopeUser | ChannelSettings | UserProfile | skipped: #2 (Default) ScopeUsers Overridden, #4 (Company 100) ScopeUsers Overridden"
        },
        { "T4", "B", "users 1", "c:100 / Email", "#10 (Company 100) Company | Company | CompanyDefaultLanguage | CompanyProfile | skipped: " },
        {
            "T5", "D", "users 2", "u:2 / Email",
            "#11 (Company 100) NamedUsers | NamedUser | UserUiLanguage | UserProfile | skipped: #12 (User 2) ScopeUsers Merged"
        },
        { "T6", "C", "company 100", "c:100 / SignalR", "#3 (Default) ScopeUsers mandatory | CompanyGroup | CompanyDefaultLanguage | None | skipped: " },
        {
            "T8", "C", "users 1", "u:1 / SignalR",
            "#3 (Default) ScopeUsers mandatory | ScopeUser | UserUiLanguage | None | skipped: #24 (User 1) ScopeUsers Locked"
        },
    };

    [Theory]
    [MemberData(nameof(TraceRows))]
    public void Resolve_Delivery_GivesTheWholeTraceOfTheExample(string example, string type, string scope, string delivery, string expected)
    {
        // Arrange
        var subscriptions = SubscriptionsOfTraceExample(example, type);

        // Act
        var resolution = Resolve(type, scope, subscriptions);

        // Assert
        var route = resolution.Deliveries.Single(route => Scenario.Describe(route).StartsWith(delivery + " /", StringComparison.Ordinal));
        Assert.Equal((example, expected), (example, Describe((ResolutionTrace)route.Trace!)));
    }

    [Fact]
    public void Resolve_OptOutOfTheCompany_GivesTheWholeOptOutDecision_T7()
    {
        // Arrange
        var subscriptions = Scenario.SubscriptionsOf("B");

        // Act
        var resolution = Resolve("B", "users 2", subscriptions);

        // Assert
        var decision = Assert.Single(resolution.OptOuts);
        Assert.Equal(
            "u:2 / Email | opt-out #4 (Company 100) ScopeUsers | skipped: #2 (Default) ScopeUsers Overridden",
            $"u:{decision.Target.Id} / {decision.Channel.Name} | opt-out {Source(decision.OptOut)} | skipped: {Skipped(decision.Skipped)}");
    }

    [Fact]
    public void Resolve_NothingSkipped_GivesTheSharedEmptyArray_T2()
    {
        // Arrange
        var subscriptions = Scenario.SubscriptionsOf("A");

        // Act
        var resolution = Resolve("A", "users 1", subscriptions);

        // Assert
        Assert.Same(Array.Empty<SkippedSubscription>(), ((ResolutionTrace)Assert.Single(resolution.Deliveries).Trace!).Skipped);
    }

    private static IReadOnlyList<Subscription> SubscriptionsOfTraceExample(string example, string type) => example switch
    {
        "T1" => _t1Subscriptions,
        "T8" => Scenario.SubscriptionsOf(type, [.. Scenario.Subscriptions, _t8User1]),
        _ => Scenario.SubscriptionsOf(type),
    };

    private static string Describe(ResolutionTrace trace) =>
        $"{Source(trace.Applied)} | {trace.Target} | {trace.Language} | {trace.Address} | skipped: {Skipped(trace.Skipped)}";

    private static string Skipped(IReadOnlyList<SkippedSubscription> skipped) =>
        string.Join(", ", skipped.Select(static subscription => $"{Source(subscription.Subscription)} {subscription.Reason}"));

    private static string Source(SubscriptionSource source) =>
        $"{Scenario.Label(source.Id)} ({Owner(source.Owner)}) {source.Recipients}{(source.Mandatory ? " mandatory" : "")}";

    private static string Owner(SubscriptionOwner owner) => owner.Level switch
    {
        SubscriptionLevel.Default => "Default",
        SubscriptionLevel.Company => $"Company {owner.Company!.Value.Value}",
        _ => $"User {owner.User!.Value.Value}",
    };

    private static string Target(DeliveryTarget target) => $"{(target.Kind == DeliveryTargetKind.User ? "u" : "c")}:{target.Id}";
}
