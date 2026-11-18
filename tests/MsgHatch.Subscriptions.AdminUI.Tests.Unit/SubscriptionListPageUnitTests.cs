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
        Assert.Equal(["Company 42", "Requests", "Channel", "Fax", "Scope users", "No", "3"], rows[0].Children.SkipLast(1).Select(static cell => cell.TextContent));
        Assert.Equal(["Edit", "Delete"], rows[0].Children[^1].Children.Select(static button => button.TextContent));
        Assert.Equal(["User 7", "Notices", "OptOut", "All channels"], rows[1].Children.Take(4).Select(static cell => cell.TextContent));
        Assert.Equal("msghatch/subscriptions/3f2c6b1e-7a40-4c1d-9a7e-1b2c3d4e5f60", rows[0].QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void Render_Page_HasTheListLayoutWithTheFiltersTheNewButtonAndTheLinks()
    {
        GivenSubscriptions(Subscription());

        var cut = Render<SubscriptionListPage>();

        Assert.Equal(
            ("Subscriptions", 3, "+ New subscription", 2, true),
            (cut.Find("[data-list-header] h1").TextContent, cut.FindAll("[data-list-filters] select").Count,
                cut.Find("[data-list-actions] [data-action=new]").TextContent, cut.FindAll("[data-list-header] a.btn-link").Count,
                cut.Find("[data-list-body]").QuerySelector("[data-list-scroll] table") is not null));
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

    [Fact]
    public void ClickNew_List_OpensTheEditorInADialogWithCancelOnTheLeftAndCreateOnTheRight()
    {
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();
        Assert.Empty(cut.FindAll(".modal"));

        cut.Find("[data-list-actions] [data-action=new]").Click();

        Assert.Equal(
            ("New subscription", true, "cancel", "save", "Create", 0),
            (cut.Find(".modal-title").TextContent, cut.Find(".modal-dialog").ClassList.Contains("modal-lg"),
                cut.Find(".modal-body [data-actions-start] button").GetAttribute("data-action"), cut.Find(".modal-body [data-actions-end] button").GetAttribute("data-action"),
                cut.Find(".modal-body [data-action=save]").TextContent, cut.FindAll(".modal-body [data-action=delete]").Count));
    }

    [Fact]
    public void ClickCreate_NamedUsersInTheDialog_CreatesTheDraftClosesTheDialogAndLoadsTheListAgain()
    {
        GivenSubscriptions();
        Commands.CreateAsync(Arg.Any<SubscriptionDraft>(), Arg.Any<CancellationToken>()).Returns(Ok(Subscription()));
        var cut = Render<SubscriptionListPage>();
        cut.Find("[data-action=new]").Click();
        cut.Find("#msghatch-edit-settings").Change("""{"number":"1"}""");
        cut.Find("#msghatch-edit-recipients").Change("NamedUsers");
        cut.Find("#msghatch-edit-users").Change("1\n 2 \n");

        cut.Find(".modal [data-action=save]").Click();

        var expected = new SubscriptionDraft(SubscriptionOwner.Default, Requests, SubscriptionKind.Channel, Fax, new FaxSettings("1"),
            SubscriptionRecipients.ForUsers([new UserId("1"), new UserId("2")]));
        Commands.Received(1).CreateAsync(expected, Arg.Any<CancellationToken>());
        Queries.ReceivedWithAnyArgs(2).GetSubscriptionsAsync(default!, Arg.Any<CancellationToken>());
        Assert.Equal((0, "http://localhost/"), (cut.FindAll(".modal").Count, Navigation.Uri));
    }

    [Fact]
    public void ClickCreate_RejectedInTheDialog_ShowsTheTextOfRejectedAndKeepsTheDialog()
    {
        GivenSubscriptions();
        Commands.CreateAsync(Arg.Any<SubscriptionDraft>(), Arg.Any<CancellationToken>()).Returns(Fail<Subscription>(Errors.Rejected));
        var cut = Render<SubscriptionListPage>();
        cut.Find("[data-action=new]").Click();
        cut.Find("#msghatch-edit-settings").Change("""{"number":"1"}""");

        cut.Find(".modal [data-action=save]").Click();

        Assert.Equal((AdminErrors.RejectedText, 1), (cut.Find(".modal .alert-danger").TextContent, cut.FindAll(".modal").Count));
    }

    [Fact]
    public void ClickCancel_Dialog_ClosesItAndCreatesNothing()
    {
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();
        cut.Find("[data-action=new]").Click();

        cut.Find(".modal [data-action=cancel]").Click();

        Assert.Empty(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().CreateAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_AddressOfNew_OpensTheDialogAndCancelLeavesTheListAddressWithNoQuery()
    {
        GivenSubscriptions();
        Navigation.NavigateTo(SubscriptionAdminRoutes.New.TrimStart('/'));
        var cut = Render<SubscriptionListPage>();
        var opened = cut.FindAll(".modal").Count;

        cut.Find(".modal [data-action=cancel]").Click();

        Assert.Equal((1, 0, "http://localhost/msghatch/subscriptions"), (opened, cut.FindAll(".modal").Count, Navigation.Uri));
    }

    [Fact]
    public void ClickDelete_Row_ShowsTheQuestionWithTheDetailsOfThatRow()
    {
        GivenSubscriptions(Subscription());
        var cut = Render<SubscriptionListPage>();

        cut.Find("tr[data-row] [data-action=delete]").Click();

        Assert.Equal(
            "owner: Company 42, type: Requests, kind: Channel, channel: Fax, recipients: Scope users, mandatory: No, revision: 3",
            string.Join(", ", cut.FindAll(".modal [data-delete-details] [data-detail]").Select(static dd => $"{dd.GetAttribute("data-detail")}: {dd.TextContent}")));
        Assert.Single(cut.FindAll(".modal [data-delete-effect]"));
    }

    [Fact]
    public void ClickConfirmDelete_Row_DeletesTheSubscriptionAndLoadsTheListAgain()
    {
        GivenSubscriptions(Subscription());
        Commands.DeleteAsync(Id, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(null));
        var cut = Render<SubscriptionListPage>();
        cut.Find("tr[data-row] [data-action=delete]").Click();

        cut.Find("[data-action=confirm-delete]").Click();

        Commands.Received(1).DeleteAsync(Id, Arg.Any<CancellationToken>());
        Queries.ReceivedWithAnyArgs(2).GetSubscriptionsAsync(default!, Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll(".modal"));
    }

    [Fact]
    public void ClickCancelDelete_Row_DeletesNothing()
    {
        GivenSubscriptions(Subscription());
        var cut = Render<SubscriptionListPage>();
        cut.Find("tr[data-row] [data-action=delete]").Click();

        cut.Find("[data-action=cancel-delete]").Click();

        Assert.Empty(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().DeleteAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickConfirmDelete_Rejected_ShowsTheTextOfRejectedAboveTheList()
    {
        GivenSubscriptions(Subscription());
        Commands.DeleteAsync(Id, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(new Error(Errors.Rejected)));
        var cut = Render<SubscriptionListPage>();
        cut.Find("tr[data-row] [data-action=delete]").Click();

        cut.Find("[data-action=confirm-delete]").Click();

        Assert.Equal((AdminErrors.RejectedText, 1), (cut.Find("[data-list-body] > .alert-danger").TextContent, cut.FindAll("tr[data-row]").Count));
    }

    private void GivenSubscriptions(params Subscription[] subscriptions) =>
        Queries.GetSubscriptionsAsync(Arg.Any<SubscriptionQuery>(), Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<Subscription>>(subscriptions));
}
