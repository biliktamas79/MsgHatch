using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Resolution;

/// <summary>The resolution examples R1 to R18 of <c>docs/subscription-domain.md</c>, on the shared scenario.</summary>
public sealed partial class SubscriptionResolverUnitTests
{
    private const string _defaultLanguage = "en";

    public static TheoryData<string, string, string, string[]> ResolutionRows => new()
    {
        { "R1", "A", "users 1", ["u:1 / Email / en / Default"] },
        { "R2", "A", "users 2", ["u:2 / Email / hu / Default"] },
        { "R3", "B", "users 1", ["u:1 / Email / en / User", "c:100 / Email / hu / Company"] },
        { "R4", "B", "users 2", ["c:100 / Email / hu / Company"] },
        { "R5", "C", "users 1,2", ["u:1 / SignalR / en / Default", "u:2 / SignalR / hu / Default"] },
        { "R6", "D", "users 1", ["u:2 / Email / hu / Company"] },
        { "R7", "E", "users 1", [] },
        { "R8", "E", "users 2", ["u:2 / Email / en / Company"] },
        { "R9", "A", "users 4", ["u:4 / Email / de / Default"] },
        { "R10", "A", "users 1,1,2", ["u:1 / Email / en / Default", "u:2 / Email / hu / Default"] },
        { "R11", "C", "company 100", ["c:100 / SignalR / hu / Default"] },
        { "R12", "D", "company 200", ["c:200 / SignalR / en / Company", "u:3 / Email / en / Company"] },
        { "R13", "D", "users 3", ["u:3 / SignalR / en / Company", "u:3 / Email / en / Company"] },
        { "R14", "A", "users 99", [] },
        { "R15", "D", "users 2", ["u:2 / Email / hu / Company"] },
        { "R16", "B", "company 100", ["u:1 / Email / en / User", "c:100 / Email / hu / Company"] },
        { "R17", "B", "users 1,2", ["u:1 / Email / en / User", "c:100 / Email / hu / Company"] },
    };

    [Theory]
    [MemberData(nameof(ResolutionRows))]
    public void Resolve_ScenarioEvent_GivesTheDeliveriesOfTheExample(string example, string type, string scope, string[] expected)
    {
        // Arrange
        var subscriptions = Scenario.SubscriptionsOf(type);

        // Act
        var resolution = Resolve(type, scope, subscriptions);

        // Assert
        Assert.Equal((example, Sorted(expected)), (example, Sorted(resolution.Deliveries.Select(Scenario.Describe))));
    }

    [Fact]
    public void Resolve_NamedUserOfAnotherCompany_IsSkipped_R18()
    {
        // Arrange
        var namingUser3 = Scenario.S11 with { Recipients = SubscriptionRecipients.ForUsers([new UserId("3")]) };
        var subscriptions = Scenario.SubscriptionsOf("D", [.. Scenario.Subscriptions.Where(static s => s != Scenario.S11), namingUser3]);

        // Act
        var resolution = Resolve("D", "users 1", subscriptions);

        // Assert
        Assert.Empty(resolution.Deliveries);
    }

    [Fact]
    public void Resolve_OptOutForAllChannels_GivesAnOptOutDecisionForEachChannel_R7()
    {
        // Arrange
        var subscriptions = Scenario.SubscriptionsOf("E");

        // Act
        var resolution = Resolve("E", "users 1", subscriptions);

        // Assert
        Assert.Equal(
            ["u:1 / Email / #7", "u:1 / Broker / #7"],
            resolution.OptOuts.Select(static decision => $"u:{decision.Target.Id} / {decision.Channel.Name} / {Scenario.Label(decision.OptOut.Id)}"));
    }

    [Fact]
    public void Resolve_SubscriptionOfAChannelThatTheTypeHasNotRegistered_IsSkipped()
    {
        // Arrange
        var forTheCompanyOnSignalR = Scenario.S10 with
        {
            Id = Scenario.LabelId(30), Type = new MessageType("A"), Channel = SignalR.SignalRChannel.Id, Settings = new SignalR.SignalRChannelSettings(),
        };

        // Act
        var resolution = Resolve("A", "users 1", [Scenario.S1, forTheCompanyOnSignalR]);

        // Assert
        Assert.Equal(["u:1 / Email / en / Default"], resolution.Deliveries.Select(Scenario.Describe));
    }

    [Fact]
    public void Resolve_AppliedSubscription_GivesItsSettingsWholeAndTheCompanyOfTheTargetAsTheTemplateScope_R3()
    {
        // Arrange
        var subscriptions = Scenario.SubscriptionsOf("B");

        // Act
        var resolution = Resolve("B", "users 1", subscriptions);

        // Assert
        Assert.Equal(
            [("User", "100", true), ("Company", "100", false)],
            resolution.Deliveries.Select(static route => (route.Target.Kind.ToString(), route.TemplateScope, ReferenceEquals(route.Settings, Scenario.S6.Settings))));
    }

    [Fact]
    public void Resolve_UserWithNoCompany_GivesNoTemplateScope_R9()
    {
        // Arrange
        var subscriptions = Scenario.SubscriptionsOf("A");

        // Act
        var resolution = Resolve("A", "users 4", subscriptions);

        // Assert
        Assert.Null(Assert.Single(resolution.Deliveries).TemplateScope);
    }

    [Fact]
    public void Resolve_NoLanguageInTheSettingsOrTheProfile_GivesTheDefaultLanguage()
    {
        // Arrange
        UserProfile[] users = [Scenario.Users[0] with { UiLanguage = null }];

        // Act
        var resolution = SubscriptionResolver.Resolve(
            Scenario.ChannelsOf("A"), Scenario.ScopeOf("users 1"), [Scenario.S1], users, Scenario.Companies, "fr");

        // Assert
        var route = Assert.Single(resolution.Deliveries);
        Assert.Equal(("fr", LanguageSource.LibraryDefault), (route.Language, ((ResolutionTrace)route.Trace!).Language));
    }

    [Fact]
    public void Resolve_CompanyScopeOfAnUnknownCompany_GivesTheGroupDeliveryInTheDefaultLanguage()
    {
        // Arrange
        var subscriptions = Scenario.SubscriptionsOf("C");

        // Act
        var resolution = Resolve("C", "company 999", subscriptions);

        // Assert
        var route = Assert.Single(resolution.Deliveries);
        Assert.Equal(
            ("c:999 / SignalR / en / Default", LanguageSource.LibraryDefault, AddressSource.None),
            (Scenario.Describe(route), ((ResolutionTrace)route.Trace!).Language, ((ResolutionTrace)route.Trace!).Address));
    }

    [Fact]
    public void Resolve_CompanyRecipientsOfAnUnknownCompany_GivesNoAddress()
    {
        // Arrange
        var forTheCompany = Scenario.S10 with { Owner = SubscriptionOwner.Default };
        UserProfile[] users = [new(new UserId("7"), new CompanyId("999"), "en", new Dictionary<ChannelId, string>())];

        // Act
        var resolution = SubscriptionResolver.Resolve(
            Scenario.ChannelsOf("B"), Scenario.ScopeOf("users 7"), [forTheCompany], users, Scenario.Companies, _defaultLanguage);

        // Assert
        var route = Assert.Single(resolution.Deliveries);
        Assert.Equal(("c:999 / Email / en / Default", null, AddressSource.None), (Scenario.Describe(route), route.Address, ((ResolutionTrace)route.Trace!).Address));
    }

    [Fact]
    public void Resolve_TwoFixedSubscriptionsForOneTarget_TheHigherLevelApplies()
    {
        // Arrange
        var defaultForUser2 = Scenario.S11 with { Id = Scenario.LabelId(31), Owner = SubscriptionOwner.Default };

        // Act
        var resolution = Resolve("D", "users 2", [defaultForUser2, Scenario.S11, Scenario.S12]);

        // Assert
        var trace = (ResolutionTrace)Assert.Single(resolution.Deliveries).Trace!;
        Assert.Equal(("#31", "#11 Merged, #12 Merged"), (Scenario.Label(trace.Applied.Id), string.Join(", ", trace.Skipped.Select(static s => $"{Scenario.Label(s.Subscription.Id)} {s.Reason}"))));
    }

    [Fact]
    public void Resolve_NamedUsersAndMandatoryOnOneLevel_TheNamedUsersApply()
    {
        // Arrange
        var mandatory = Scenario.S1 with { Mandatory = true };
        var forUser1 = Scenario.S1 with { Id = Scenario.LabelId(32), Recipients = SubscriptionRecipients.ForUsers([new UserId("1")]) };

        // Act
        var resolution = Resolve("A", "users 1", [mandatory, forUser1]);

        // Assert
        var trace = (ResolutionTrace)Assert.Single(resolution.Deliveries).Trace!;
        Assert.Equal(("#32", "#1 Merged"), (Scenario.Label(trace.Applied.Id), string.Join(", ", trace.Skipped.Select(static s => $"{Scenario.Label(s.Subscription.Id)} {s.Reason}"))));
    }

    [Fact]
    public void Resolve_OptOutOfAUserWithAFixedSubscription_TheFixedSubscriptionApplies()
    {
        // Arrange
        var optOut = Scenario.S7 with { Id = Scenario.LabelId(33), Owner = SubscriptionOwner.ForUser(new UserId("2")), Type = new MessageType("D") };

        // Act
        var resolution = Resolve("D", "users 2", [Scenario.S11, optOut]);

        // Assert
        var trace = (ResolutionTrace)Assert.Single(resolution.Deliveries).Trace!;
        Assert.Equal(
            ("#11", "#33 Merged", 2),
            (Scenario.Label(trace.Applied.Id), string.Join(", ", trace.Skipped.Select(static s => $"{Scenario.Label(s.Subscription.Id)} {s.Reason}")), resolution.OptOuts.Count));
    }

    [Fact]
    public void Resolve_ChannelSubscriptionWithNoSettings_IsSkipped()
    {
        // Arrange
        var noSettings = Scenario.S1 with { Settings = null };

        // Act
        var resolution = Resolve("A", "users 1", [noSettings]);

        // Assert
        Assert.Empty(resolution.Deliveries);
    }

    [Fact]
    public void Resolve_EmailSettingsOfAnotherLanguage_GiveTheLanguageOfTheSettings()
    {
        // Arrange
        var hungarian = Scenario.S1 with { Settings = new EmailChannelSettings("hu") };

        // Act
        var resolution = Resolve("A", "users 1", [hungarian]);

        // Assert
        Assert.Equal(["u:1 / Email / hu / Default"], resolution.Deliveries.Select(Scenario.Describe));
    }

    private static SubscriptionResolution Resolve(string type, string scope, IReadOnlyList<Subscription> subscriptions) =>
        SubscriptionResolver.Resolve(
            Scenario.ChannelsOf(type), Scenario.ScopeOf(scope), subscriptions, Scenario.Users, Scenario.Companies, _defaultLanguage);

    private static string Sorted(IEnumerable<string> deliveries) => string.Join("; ", deliveries.Order(StringComparer.Ordinal));
}
