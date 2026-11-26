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
        // Arrange
        var optOut = Subscription(SubscriptionOwner.ForUser(new UserId("7")), Notices, SubscriptionKind.OptOut, id: Guid.Empty) with { Channel = null, Settings = null };
        GivenSubscriptions(Subscription(), optOut);

        // Act
        var cut = Render<SubscriptionListPage>();

        // Assert
        var rows = cut.FindAll("tr[data-row]");
        Assert.Equal(["Company 42", "Requests", "Channel", "Fax", "Scope users", "No", "3"], rows[0].Children.SkipLast(1).Select(static cell => cell.TextContent));
        Assert.Equal(["Edit", "Delete"], rows[0].Children[^1].Children.Select(static button => button.TextContent));
        Assert.Equal(["User 7", "Notices", "OptOut", "All channels"], rows[1].Children.Take(4).Select(static cell => cell.TextContent));
        Assert.Equal("msghatch/subscriptions/3f2c6b1e-7a40-4c1d-9a7e-1b2c3d4e5f60", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Page_HasTheListLayoutWithTheFiltersTheNewButtonAndTheLink()
    {
        // Arrange
        GivenSubscriptions(Subscription());

        // Act
        var cut = Render<SubscriptionListPage>();

        // Assert
        Assert.Equal(
            ("Subscriptions", 5, "+ New subscription", 1, true),
            (cut.Find("[data-list-header] h1").TextContent, cut.FindAll("[data-list-filters] select").Count,
                cut.Find("[data-list-actions] [data-action=new]").TextContent, cut.FindAll("[data-list-header] a.btn-link").Count,
                cut.Find("[data-list-body]").QuerySelector("[data-list-scroll] table") is not null));
    }

    [Fact]
    public void Render_Page_LinksOnlyToTheRoutingVerification()
    {
        // Act
        var cut = Render<SubscriptionListPage>();

        // Assert
        var link = Assert.Single(cut.FindAll("[data-list-header] a.btn-link"));
        Assert.Equal(("Routing verification", SubscriptionAdminRoutes.Verification.TrimStart('/')), (link.TextContent, link.GetAttribute("href")));
    }

    [Fact]
    public void Render_CompanyAndNamedUsersRecipients_ShowsTheTextOfEachKind()
    {
        // Arrange
        GivenSubscriptions(Subscription(recipients: SubscriptionRecipients.Company),
            Subscription(recipients: SubscriptionRecipients.ForUsers([new UserId("1"), new UserId("2")]), id: Guid.Empty));

        // Act
        var cut = Render<SubscriptionListPage>();

        // Assert
        Assert.Equal(["Company", "2 named users"], cut.FindAll("tr[data-row]").Select(static row => row.Children[4].TextContent));
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_NoSubscription_ShowsTheEmptyState()
    {
        // Arrange
        GivenSubscriptions();

        // Act
        var cut = Render<SubscriptionListPage>();

        // Assert
        Assert.Equal("There are no subscriptions.", cut.Find("[data-state=empty]").TextContent);
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        // Arrange
        Queries.GetSubscriptionsAsync(Arg.Any<SubscriptionQuery>(), Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<Subscription>>(Errors.Unavailable));

        // Act
        var cut = Render<SubscriptionListPage>();

        // Assert
        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        // Arrange
        Queries.GetSubscriptionsAsync(Arg.Any<SubscriptionQuery>(), Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<Subscription>>(Errors.Rejected));

        // Act
        var cut = Render<SubscriptionListPage>();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Filters_OffersTheMessageTypesAndTheChannelsOfTheHost()
    {
        // Arrange
        GivenSubscriptions();

        // Act
        var cut = Render<SubscriptionListPage>();

        // Assert
        Assert.Equal(["", "Requests", "Notices"], cut.FindAll("#msghatch-subscription-type option").Select(static option => option.GetAttribute("value")));
        Assert.Equal(["", "Fax", "Pager"], cut.FindAll("#msghatch-subscription-channel option").Select(static option => option.GetAttribute("value")));
    }

    [Fact]
    public void Render_Filters_OffersEachKindAndBothValuesOfMandatory()
    {
        // Arrange
        GivenSubscriptions();

        // Act
        var cut = Render<SubscriptionListPage>();

        // Assert
        Assert.Equal(["", "Channel", "OptOut"], cut.FindAll("#msghatch-subscription-kind option").Select(static option => option.GetAttribute("value")));
        Assert.Equal(["", "Yes", "No"], cut.FindAll("#msghatch-subscription-mandatory option").Select(static option => option.GetAttribute("value")));
    }

    [Fact]
    public void Change_KindAndMandatory_ShowsOnlyTheSubscriptionsThatMatchBoth()
    {
        // Arrange
        GivenSubscriptions(
            Subscription() with { Mandatory = true },
            Subscription(id: Guid.Empty),
            Subscription(kind: SubscriptionKind.OptOut, id: new Guid("00000000-0000-0000-0000-000000000002")) with { Mandatory = true, Channel = null, Settings = null });
        var cut = Render<SubscriptionListPage>();

        // Act
        cut.Find("#msghatch-subscription-kind").Change("Channel");
        cut.Find("#msghatch-subscription-mandatory").Change("Yes");

        // Assert
        var row = Assert.Single(cut.FindAll("tr[data-row]"));
        Assert.Equal(("Channel", "Yes"), (row.Children[2].TextContent, row.Children[5].TextContent));
        Queries.Received(3).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Change_LevelAndMessageType_QueriesTheSubscriptionsWithBothFilters()
    {
        // Arrange
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();

        // Act
        cut.Find("#msghatch-subscription-level").Change("Company");
        cut.Find("#msghatch-subscription-type").Change("Requests");

        // Assert
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(Type: Requests, Level: SubscriptionLevel.Company), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_AddressOfTheListOfAMessageTypeAndALevel_StartsWithBothFilters()
    {
        // Arrange
        GivenSubscriptions();
        Navigation.NavigateTo(SubscriptionAdminRoutes.ListOf(Requests, SubscriptionLevel.Company).TrimStart('/'));

        // Act
        var cut = Render<SubscriptionListPage>();

        // Assert
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(Type: Requests, Level: SubscriptionLevel.Company), Arg.Any<CancellationToken>());
        Assert.Equal(
            ("Company", "Requests"),
            (cut.Find("#msghatch-subscription-level").GetAttribute("value"), cut.Find("#msghatch-subscription-type").GetAttribute("value")));
    }

    [Theory]
    [InlineData("msghatch/subscriptions?type=not%20a%20name&level=Boss")]
    [InlineData("msghatch/subscriptions?level=1")]
    [InlineData("msghatch/subscriptions?level=company")]
    public void Render_AddressWithValuesThatAreNotValid_StartsWithNoFilter(string address)
    {
        // Arrange
        GivenSubscriptions();
        Navigation.NavigateTo(address);

        // Act
        Render<SubscriptionListPage>();

        // Assert
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Change_MessageTypeThatIsNotAName_ShowsValidationFailed()
    {
        // Arrange
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();

        // Act
        cut.Find("#msghatch-subscription-type").Change("not a name");

        // Assert
        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Change_LevelWhileAnEarlierQueryRuns_ShowsOnlyTheAnswerOfTheLastQuery()
    {
        // Arrange
        var earlier = new TaskCompletionSource<ErrorOr<IReadOnlyList<Subscription>>>();
        GivenSubscriptions();
        Queries.GetSubscriptionsAsync(new SubscriptionQuery(Level: SubscriptionLevel.Company), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ErrorOr<IReadOnlyList<Subscription>>>(earlier.Task));
        Queries.GetSubscriptionsAsync(new SubscriptionQuery(Level: SubscriptionLevel.User), Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<Subscription>>([Subscription(channel: Pager)]));
        var cut = Render<SubscriptionListPage>();

        // Act
        cut.Find("#msghatch-subscription-level").Change("Company");
        cut.Find("#msghatch-subscription-level").Change("User");
        await cut.InvokeAsync(() => earlier.SetResult(ErrorOr<IReadOnlyList<Subscription>>.ForValue([Subscription()])));

        // Assert
        Assert.Equal(["Pager"], cut.FindAll("tr[data-row]").Select(static row => row.Children[3].TextContent));
        await Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(Level: SubscriptionLevel.Company), Arg.Any<CancellationToken>());
        await Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(Level: SubscriptionLevel.User), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Change_Channel_ShowsOnlyTheSubscriptionsOfTheChannel()
    {
        // Arrange
        GivenSubscriptions(Subscription(), Subscription(channel: Pager, id: Guid.Empty));
        var cut = Render<SubscriptionListPage>();

        // Act
        cut.Find("#msghatch-subscription-channel").Change("Pager");

        // Assert
        var row = Assert.Single(cut.FindAll("tr[data-row]"));
        Assert.Equal("Pager", row.Children[3].TextContent);
        Queries.Received(2).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickNew_List_OpensTheEditorInADialogWithCancelOnTheLeftAndCreateOnTheRight()
    {
        // Arrange
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();
        Assert.Empty(cut.FindAll(".modal"));

        // Act
        cut.Find("[data-list-actions] [data-action=new]").Click();

        // Assert
        Assert.Equal(
            ("New subscription", true, "cancel", "save", "Create", 0),
            (cut.Find(".modal-title").TextContent, cut.Find(".modal-dialog").ClassList.Contains("modal-lg"),
                cut.Find(".modal-body [data-actions-start] button").GetAttribute("data-action"), cut.Find(".modal-body [data-actions-end] button").GetAttribute("data-action"),
                cut.Find(".modal-body [data-action=save]").TextContent, cut.FindAll(".modal-body [data-action=delete]").Count));
    }

    [Fact]
    public void ClickNew_List_FillsTheSettingsWithAnEmptyObject()
    {
        // Arrange
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();

        // Act
        cut.Find("[data-action=new]").Click();

        // Assert
        Assert.Equal("{}", cut.Find("#msghatch-edit-settings").GetAttribute("value"));
    }

    [Fact]
    public void ChangeMessageType_NewSubscription_OffersOnlyItsChannelsAndSelectsTheFirst()
    {
        // Arrange
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();
        cut.Find("[data-action=new]").Click();

        // Act
        cut.Find("#msghatch-edit-type").Change("Notices");

        // Assert
        Assert.Equal(
            (",Pager", "Pager"),
            (string.Join(',', cut.FindAll("#msghatch-edit-channel option").Select(static option => option.GetAttribute("value"))),
                cut.Find("#msghatch-edit-channel").GetAttribute("value")));
    }

    [Fact]
    public void ClickCreate_NamedUsersInTheDialog_CreatesTheDraftClosesTheDialogAndLoadsTheListAgain()
    {
        // Arrange
        GivenSubscriptions();
        Commands.CreateAsync(Arg.Any<SubscriptionDraft>(), Arg.Any<CancellationToken>()).Returns(Ok(Subscription()));
        var cut = Render<SubscriptionListPage>();
        cut.Find("[data-action=new]").Click();
        cut.Find("#msghatch-edit-settings").Change("""{"number":"1"}""");
        cut.Find("#msghatch-edit-recipients").Change("NamedUsers");
        cut.Find("#msghatch-edit-users").Change("1\n 2 \n");

        // Act
        cut.Find(".modal [data-action=save]").Click();

        // Assert
        var expected = new SubscriptionDraft(SubscriptionOwner.Default, Requests, SubscriptionKind.Channel, Fax, new FaxSettings("1"),
            SubscriptionRecipients.ForUsers([new UserId("1"), new UserId("2")]));
        Commands.Received(1).CreateAsync(expected, Arg.Any<CancellationToken>());
        Queries.Received(2).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
        Assert.Equal((0, "http://localhost/"), (cut.FindAll(".modal").Count, Navigation.Uri));
    }

    [Fact]
    public void ClickCreate_OptOutWithTheDefaultSettingsInTheDialog_CreatesADraftWithNoSettings()
    {
        // Arrange
        GivenSubscriptions();
        Commands.CreateAsync(Arg.Any<SubscriptionDraft>(), Arg.Any<CancellationToken>()).Returns(Ok(Subscription()));
        var cut = Render<SubscriptionListPage>();
        cut.Find("[data-action=new]").Click();
        cut.Find("#msghatch-edit-kind").Change("OptOut");

        // Act
        cut.Find(".modal [data-action=save]").Click();

        // Assert
        var expected = new SubscriptionDraft(SubscriptionOwner.Default, Requests, SubscriptionKind.OptOut, Fax, Settings: null, SubscriptionRecipients.ScopeUsers);
        Commands.Received(1).CreateAsync(expected, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickCreate_ConflictInTheDialog_ShowsTheMessageOfTheConflictAndKeepsTheDialog()
    {
        // Arrange
        GivenSubscriptions();
        const string duplicate = "The owner already has a subscription for the message type, the channel, and the recipient kind.";
        Commands.CreateAsync(Arg.Any<SubscriptionDraft>(), Arg.Any<CancellationToken>()).Returns(
            ValueTask.FromResult(ErrorOr<Subscription>.ForError(new Error(Errors.Conflict, duplicate))));
        var cut = Render<SubscriptionListPage>();
        cut.Find("[data-action=new]").Click();
        cut.Find("#msghatch-edit-kind").Change("OptOut");

        // Act
        cut.Find(".modal [data-action=save]").Click();

        // Assert
        var alert = cut.Find(".modal [data-actions-middle] .alert-danger");
        Assert.Equal((duplicate, "409", 1, 1), (alert.TextContent, alert.GetAttribute("data-error-code"), cut.FindAll(".modal").Count, cut.FindAll(".alert-danger").Count));
        var expected = new SubscriptionDraft(SubscriptionOwner.Default, Requests, SubscriptionKind.OptOut, Fax, Settings: null, SubscriptionRecipients.ScopeUsers);
        Commands.Received(1).CreateAsync(expected, Arg.Any<CancellationToken>());
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickNew_List_ShowsTheHelpOfTheRecipientsInAFullWidthColumn()
    {
        // Arrange
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();

        // Act
        cut.Find("[data-action=new]").Click();

        // Assert
        Assert.True(cut.Find("[data-field=recipients-help]").ParentElement!.ClassList.Contains("col-12"));
    }

    [Fact]
    public void ChangeKind_OptOut_DisablesTheSettingsAndTellsThatAnOptOutHasNoSettings()
    {
        // Arrange
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();
        cut.Find("[data-action=new]").Click();

        // Act
        cut.Find("#msghatch-edit-kind").Change("OptOut");

        // Assert
        Assert.Equal(
            (true, "An opt-out has no settings, so the save sends none."),
            (cut.Find("#msghatch-edit-settings").HasAttribute("disabled"), cut.Find("[data-field=settings-type]").TextContent));
    }

    [Fact]
    public void ClickCreate_RejectedInTheDialog_ShowsTheTextOfRejectedAndKeepsTheDialog()
    {
        // Arrange
        GivenSubscriptions();
        Commands.CreateAsync(Arg.Any<SubscriptionDraft>(), Arg.Any<CancellationToken>()).Returns(Fail<Subscription>(Errors.Rejected));
        var cut = Render<SubscriptionListPage>();
        cut.Find("[data-action=new]").Click();
        cut.Find("#msghatch-edit-settings").Change("""{"number":"1"}""");

        // Act
        cut.Find(".modal [data-action=save]").Click();

        // Assert
        Assert.Equal((AdminErrors.RejectedText, 1), (cut.Find(".modal .alert-danger").TextContent, cut.FindAll(".modal").Count));
        var expected = new SubscriptionDraft(SubscriptionOwner.Default, Requests, SubscriptionKind.Channel, Fax, new FaxSettings("1"), SubscriptionRecipients.ScopeUsers);
        Commands.Received(1).CreateAsync(expected, Arg.Any<CancellationToken>());
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickCancel_Dialog_ClosesItAndCreatesNothing()
    {
        // Arrange
        GivenSubscriptions();
        var cut = Render<SubscriptionListPage>();
        cut.Find("[data-action=new]").Click();

        // Act
        cut.Find(".modal [data-action=cancel]").Click();

        // Assert
        Assert.Empty(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().CreateAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_AddressOfNew_OpensTheDialogAndCancelLeavesTheListAddressWithNoQuery()
    {
        // Arrange
        GivenSubscriptions();
        Navigation.NavigateTo(SubscriptionAdminRoutes.New.TrimStart('/'));

        // Act
        var cut = Render<SubscriptionListPage>();
        var opened = cut.FindAll(".modal").Count;

        cut.Find(".modal [data-action=cancel]").Click();

        // Assert
        Assert.Equal((1, 0, "http://localhost/msghatch/subscriptions"), (opened, cut.FindAll(".modal").Count, Navigation.Uri));
    }

    [Fact]
    public void ClickDelete_Row_ShowsTheQuestionWithTheDetailsOfThatRow()
    {
        // Arrange
        GivenSubscriptions(Subscription());
        var cut = Render<SubscriptionListPage>();

        // Act
        cut.Find("tr[data-row] [data-action=delete]").Click();

        // Assert
        Assert.Equal(
            "owner: Company 42, type: Requests, kind: Channel, channel: Fax, recipients: Scope users, mandatory: No, revision: 3",
            string.Join(", ", cut.FindAll(".modal [data-delete-details] [data-detail]").Select(static dd => $"{dd.GetAttribute("data-detail")}: {dd.TextContent}")));
        Assert.Single(cut.FindAll(".modal [data-delete-effect]"));
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
        Commands.DidNotReceiveWithAnyArgs().DeleteAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickConfirmDelete_Row_DeletesTheSubscriptionAndLoadsTheListAgain()
    {
        // Arrange
        GivenSubscriptions(Subscription());
        Commands.DeleteAsync(Id, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(null));
        var cut = Render<SubscriptionListPage>();
        cut.Find("tr[data-row] [data-action=delete]").Click();

        // Act
        cut.Find("[data-action=confirm-delete]").Click();

        // Assert
        Commands.Received(1).DeleteAsync(Id, Arg.Any<CancellationToken>());
        Queries.Received(2).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll(".modal"));
    }

    [Fact]
    public void ClickCancelDelete_Row_DeletesNothing()
    {
        // Arrange
        GivenSubscriptions(Subscription());
        var cut = Render<SubscriptionListPage>();
        cut.Find("tr[data-row] [data-action=delete]").Click();

        // Act
        cut.Find("[data-action=cancel-delete]").Click();

        // Assert
        Assert.Empty(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().DeleteAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickConfirmDelete_Rejected_ShowsTheTextOfRejectedAboveTheList()
    {
        // Arrange
        GivenSubscriptions(Subscription());
        Commands.DeleteAsync(Id, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(new Error(Errors.Rejected)));
        var cut = Render<SubscriptionListPage>();
        cut.Find("tr[data-row] [data-action=delete]").Click();

        // Act
        cut.Find("[data-action=confirm-delete]").Click();

        // Assert
        Assert.Equal((AdminErrors.RejectedText, 1), (cut.Find("[data-list-body] > .alert-danger").TextContent, cut.FindAll("tr[data-row]").Count));
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    private void GivenSubscriptions(params Subscription[] subscriptions) =>
        Queries.GetSubscriptionsAsync(Arg.Any<SubscriptionQuery>(), Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<Subscription>>(subscriptions));
}
