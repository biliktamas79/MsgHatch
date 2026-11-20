using Bunit;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Api.Contracts;
using NSubstitute;
using static MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes.SubscriptionTestData;

namespace MsgHatch.Subscriptions.AdminUI.Tests.Unit;

public sealed class SubscriptionEditPageUnitTests : SubscriptionPageContext
{
    private const string _faxSettingsText = "{\n  \"number\": \"123\"\n}";

    public SubscriptionEditPageUnitTests()
    {
        Queries.GetSubscriptionAsync(Id, Arg.Any<CancellationToken>()).Returns(Ok(Subscription()));
    }

    [Fact]
    public void Render_StoredSubscription_ShowsItsValuesAndTheIndentedSettings()
    {
        var cut = RenderEdit();

        Assert.Equal("Company", cut.Find("#msghatch-edit-level").GetAttribute("value"));
        Assert.Equal("42", cut.Find("#msghatch-edit-company").GetAttribute("value"));
        Assert.Equal("Requests", cut.Find("#msghatch-edit-type").GetAttribute("value"));
        Assert.Equal("Fax", cut.Find("#msghatch-edit-channel").GetAttribute("value"));
        Assert.Equal(_faxSettingsText, cut.Find("#msghatch-edit-settings").GetAttribute("value"));
        Assert.Equal("3", cut.Find("[data-field=revision]").TextContent);
        Assert.Equal("This host reads the settings as FaxSettings.", cut.Find("[data-field=settings-type]").TextContent);
    }

    [Fact]
    public void Render_StoredChannelThatTheTypeDoesNotRegister_KeepsItInTheChannels()
    {
        Queries.GetSubscriptionAsync(Id, Arg.Any<CancellationToken>()).Returns(Ok(Subscription(type: Notices)));

        var cut = RenderEdit();

        Assert.Equal(
            (",Fax,Pager", "Fax"),
            (string.Join(',', cut.FindAll("#msghatch-edit-channel option").Select(static option => option.GetAttribute("value"))),
                cut.Find("#msghatch-edit-channel").GetAttribute("value")));
    }

    [Theory]
    [InlineData(RecipientKind.ScopeUsers, "Each user that the owner covers gets the message")]
    [InlineData(RecipientKind.Company, "One message goes to the address of the company")]
    [InlineData(RecipientKind.NamedUsers, "Each named user gets the message")]
    public void Render_RecipientKind_ShowsTheHelpOfTheKind(RecipientKind recipients, string start)
    {
        var cut = RenderEdit();

        cut.Find("#msghatch-edit-recipients").Change(recipients.ToString());

        Assert.StartsWith(start, cut.Find("[data-field=recipients-help]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_StoredSubscription_PutsTheTitleInTheHeaderAndTheActionsUnderTheScrollingBody()
    {
        var cut = RenderEdit();

        Assert.Equal(
            ("Subscription", true, true, false),
            (cut.Find("[data-edit-header] h1").TextContent, cut.Find("[data-edit-body]").QuerySelector("[data-form=subscription]") is not null,
                cut.Find("[data-edit-actions]").QuerySelector("[data-action=save]") is not null, cut.Find("[data-edit-body]").QuerySelector("[data-action=save]") is not null));
    }

    [Fact]
    public void Render_StoredSubscription_PutsBackOnTheLeftAndDeleteAndSaveOnTheRight()
    {
        var cut = RenderEdit();

        Assert.Equal(
            ("back", SubscriptionAdminRoutes.List.TrimStart('/'), "delete save"),
            (cut.Find("[data-actions-start] a").GetAttribute("data-action"), cut.Find("[data-actions-start] a").GetAttribute("href"),
                string.Join(' ', cut.FindAll("[data-actions-end] button").Select(static button => button.GetAttribute("data-action")))));
    }

    [Fact]
    public void Render_NotFound_ShowsTheMessageOfTheError()
    {
        Queries.GetSubscriptionAsync(Id, Arg.Any<CancellationToken>()).Returns(Fail<Subscription>(Errors.NotFound, "The subscription does not exist."));

        var cut = RenderEdit();

        Assert.Equal("The subscription does not exist.", cut.Find(".alert-danger").TextContent);
        Assert.Empty(cut.FindAll("[data-action=save]"));
    }

    [Fact]
    public void Render_ChannelsUnavailable_ShowsTheTextOfUnavailable()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<ChannelDescriptor>>(Errors.Unavailable));

        var cut = RenderEdit();

        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Change_Settings_ShowsThePendingChanges()
    {
        var cut = RenderEdit();

        cut.Find("#msghatch-edit-settings").Change("""{"number":"456"}""");

        Assert.Equal("Changes that are not saved: settings.", cut.Find("[data-actions-middle] [data-state=pending]").TextContent);
    }

    [Fact]
    public void ClickSave_ChangedSettings_SendsTheExpectedRevisionAndTheTypedSettings()
    {
        var expected = new SubscriptionDraft(SubscriptionOwner.ForCompany(new CompanyId("42")), Requests, SubscriptionKind.Channel, Fax,
            new FaxSettings("456"), SubscriptionRecipients.ScopeUsers);
        Commands.UpdateAsync(Id, 3, expected, Arg.Any<CancellationToken>()).Returns(Ok(Subscription(settings: new FaxSettings("456"), revision: 4)));
        var cut = RenderEdit();
        cut.Find("#msghatch-edit-settings").Change("""{"number":"456"}""");

        cut.Find("[data-action=save]").Click();

        Commands.Received(1).UpdateAsync(Id, 3, expected, Arg.Any<CancellationToken>());
        Assert.Equal("4", cut.Find("[data-field=revision]").TextContent);
        Assert.Single(cut.FindAll("[data-actions-middle] [data-state=saved]"));
        Assert.Empty(cut.FindAll("[data-state=pending]"));
    }

    [Fact]
    public void ClickSave_ChannelWithNoSettingsTypeInTheHost_SendsTheSettingsAsJsonChannelSettings()
    {
        Commands.UpdateAsync(Id, 3, Arg.Any<SubscriptionDraft>(), Arg.Any<CancellationToken>()).Returns(Ok(Subscription()));
        var cut = RenderEdit();
        cut.Find("#msghatch-edit-channel").Change("Pager");
        cut.Find("#msghatch-edit-settings").Change("""{"code":7}""");

        cut.Find("[data-action=save]").Click();

        Commands.Received(1).UpdateAsync(Id, 3, Arg.Is<SubscriptionDraft>(draft =>
            draft.Channel == Pager && draft.Settings is JsonChannelSettings && ((JsonChannelSettings)draft.Settings).Json.GetRawText() == """{"code":7}"""),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickSave_OptOutWithNoChannelAndNoSettings_SendsNoChannelAndNoSettings()
    {
        var expected = new SubscriptionDraft(SubscriptionOwner.ForCompany(new CompanyId("42")), Requests, SubscriptionKind.OptOut, Channel: null,
            Settings: null, SubscriptionRecipients.ScopeUsers);
        Commands.UpdateAsync(Id, 3, expected, Arg.Any<CancellationToken>()).Returns(Ok(Subscription()));
        var cut = RenderEdit();
        cut.Find("#msghatch-edit-kind").Change("OptOut");
        cut.Find("#msghatch-edit-channel").Change("");
        cut.Find("#msghatch-edit-settings").Change(" ");

        cut.Find("[data-action=save]").Click();

        Commands.Received(1).UpdateAsync(Id, 3, expected, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickSave_Conflict_LoadsTheStoredVersionAgainAndShowsTheTextOfConflict()
    {
        Commands.UpdateAsync(Id, 3, Arg.Any<SubscriptionDraft>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(
            ErrorOr<Subscription>.ForError(new Error(Errors.Conflict, "The stored revision is not the expected one, or another subscription of the owner conflicts."))));
        var cut = RenderEdit();
        cut.Find("#msghatch-edit-settings").Change("""{"number":"456"}""");
        Queries.GetSubscriptionAsync(Id, Arg.Any<CancellationToken>()).Returns(Ok(Subscription(settings: new FaxSettings("789"), revision: 5)));

        cut.Find("[data-action=save]").Click();

        Assert.Equal("5", cut.Find("[data-field=revision]").TextContent);
        Assert.Equal("{\n  \"number\": \"789\"\n}", cut.Find("#msghatch-edit-settings").GetAttribute("value"));
        Assert.Equal(AdminErrors.ConflictText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void ClickSave_ValidationFailedOfTheServer_ShowsTheMessageInTheActionBar()
    {
        Commands.UpdateAsync(Id, 3, Arg.Any<SubscriptionDraft>(), Arg.Any<CancellationToken>())
            .Returns(Fail<Subscription>(Errors.ValidationFailed, "A mandatory subscription is a default or company subscription for the scope users."));
        var cut = RenderEdit();

        cut.Find("[data-action=save]").Click();

        Assert.Equal("A mandatory subscription is a default or company subscription for the scope users.", cut.Find("[data-actions-middle] .alert-danger").TextContent);
    }

    [Fact]
    public void ClickSave_SettingsThatAreNotJson_ShowsValidationFailedAndSendsNothing()
    {
        var cut = RenderEdit();
        cut.Find("#msghatch-edit-settings").Change("{ number");

        cut.Find("[data-action=save]").Click();

        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Commands.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickSave_UserLevelWithNoUserId_ShowsValidationFailedAndSendsNothing()
    {
        var cut = RenderEdit();
        cut.Find("#msghatch-edit-level").Change("User");

        cut.Find("[data-action=save]").Click();

        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Commands.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickDelete_StoredSubscription_ShowsItsDetailsAndTheEffectInTheQuestion()
    {
        var cut = RenderEdit();
        cut.Find("#msghatch-edit-company").Change("43");

        cut.Find("[data-action=delete]").Click();

        Assert.Equal(
            "owner: Company 42, type: Requests, kind: Channel, channel: Fax, recipients: Scope users, mandatory: No, revision: 3",
            string.Join(", ", cut.FindAll(".modal [data-delete-details] [data-detail]").Select(static dd => $"{dd.GetAttribute("data-detail")}: {dd.TextContent}")));
        Assert.Equal((1, 1), (cut.FindAll(".modal [data-delete-effect]").Count, cut.FindAll(".modal [data-delete-warning]").Count));
    }

    [Fact]
    public void ClickDelete_NoAnswer_DeletesNothing()
    {
        var cut = RenderEdit();

        cut.Find("[data-action=delete]").Click();

        Assert.Single(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().DeleteAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickDelete_Cancelled_DeletesNothing()
    {
        var cut = RenderEdit();
        cut.Find("[data-action=delete]").Click();

        cut.Find("[data-action=cancel-delete]").Click();

        Assert.Empty(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().DeleteAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickDelete_Confirmed_DeletesTheSubscriptionAndOpensTheList()
    {
        Commands.DeleteAsync(Id, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(null));
        var cut = RenderEdit();
        cut.Find("[data-action=delete]").Click();

        cut.Find("[data-action=confirm-delete]").Click();

        Commands.Received(1).DeleteAsync(Id, Arg.Any<CancellationToken>());
        Assert.Equal("http://localhost/msghatch/subscriptions", Navigation.Uri);
    }

    private IRenderedComponent<SubscriptionEditPage> RenderEdit() => Render<SubscriptionEditPage>(p => p.Add(x => x.Id, Id));
}
