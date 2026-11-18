using Bunit;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes.SubscriptionTestData;

namespace MsgHatch.Subscriptions.AdminUI.Tests.Unit;

public sealed class SubscriptionListPageUnitTests : SubscriptionPageContext
{
    [Fact]
    public void Render_TwoSubscriptions_ShowsARowForEachSubscriptionWithItsEditLink()
    {
        var optOut = Subscription(SubscriptionOwner.ForUser(new UserId("7")), Notices, SubscriptionKind.OptOut, id: Guid.Empty) with { Channel = null, Settings = null };
        GivenSubscriptions(Subscription(), optOut);

        var cut = Render<SubscriptionListPage>();

        var rows = cut.FindAll("tr[data-row]");
        Assert.Equal(["Company 42", "Requests", "Channel", "Fax", "Scope users", "No", "3", "Edit"], rows[0].Children.Select(static cell => cell.TextContent));
        Assert.Equal(["User 7", "Notices", "OptOut", "All channels"], rows[1].Children.Take(4).Select(static cell => cell.TextContent));
        Assert.Equal("msghatch/subscriptions/3f2c6b1e-7a40-4c1d-9a7e-1b2c3d4e5f60", rows[0].QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void Render_CompanyAndNamedUsersRecipients_ShowsTheTextOfEachKind()
    {
        GivenSubscriptions(Subscription(recipients: SubscriptionRecipients.Company),
            Subscription(recipients: SubscriptionRecipients.ForUsers([new UserId("1"), new UserId("2")]), id: Guid.Empty));

        var cut = Render<SubscriptionListPage>();

        Assert.Equal(["Company", "2 named users"], cut.FindAll("tr[data-row]").Select(static row => row.Children[4].TextContent));
    }

    [Fact]
    public void Render_NoSubscription_ShowsTheEmptyState()
    {
        GivenSubscriptions();

        var cut = Render<SubscriptionListPage>();

        Assert.Equal("There are no subscriptions.", cut.Find("[data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        Queries.GetSubscriptionsAsync(Arg.Any<SubscriptionQuery>(), Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<Subscription>>(Errors.Unavailable));

        var cut = Render<SubscriptionListPage>();

        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        Queries.GetSubscriptionsAsync(Arg.Any<SubscriptionQuery>(), Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<Subscription>>(Errors.Rejected));

        var cut = Render<SubscriptionListPage>();

        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_Filters_OffersTheMessageTypesAndTheChannelsOfTheHost()
    {
        GivenSubscriptions();

        var cut = Render<SubscriptionListPage>();

        Assert.Equal(["", "Requests", "Notices"], cut.FindAll("#msghatch-subscription-type option").Select(static option => option.GetAttribute("value")));
        Assert.Equal(["", "Fax", "Pager"], cut.FindAll("#msghatch-subscription-channel option").Select(static option => option.GetAttribute("value")));
    }

    [Fact]
    public void ClickShow_LevelAndMessageType_QueriesTheSubscriptionsWithBothFilters()
    {
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();

        cut.Find("#msghatch-subscription-level").Change("Company");
        cut.Find("#msghatch-subscription-type").Change("Requests");
        cut.Find("[data-action=filter]").Click();

        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(Type: Requests, Level: SubscriptionLevel.Company), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickShow_MessageTypeThatIsNotAName_ShowsValidationFailed()
    {
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();

        cut.Find("#msghatch-subscription-type").Change("not a name");
        cut.Find("[data-action=filter]").Click();

        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
    }

    [Fact]
    public void ClickShow_Channel_ShowsOnlyTheSubscriptionsOfTheChannel()
    {
        GivenSubscriptions(Subscription(), Subscription(channel: Pager, id: Guid.Empty));
        var cut = Render<SubscriptionListPage>();

        cut.Find("#msghatch-subscription-channel").Change("Pager");
        cut.Find("[data-action=filter]").Click();

        var row = Assert.Single(cut.FindAll("tr[data-row]"));
        Assert.Equal("Pager", row.Children[3].TextContent);
    }

    private void GivenSubscriptions(params Subscription[] subscriptions) =>
        Queries.GetSubscriptionsAsync(Arg.Any<SubscriptionQuery>(), Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<Subscription>>(subscriptions));
}
