using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

/// <summary>The edit page of the stored settings of one channel (D284, D287), on the channels of <see cref="RegistryPageContext"/>.</summary>
public sealed class ChannelEditPageUnitTests : RegistryPageContext
{
    private static readonly ChannelEntry _stored = new(Email, "Shop email", null, Enabled: false, Revision: 2, new DateTime(2029, 12, 31, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>The channel Email with a stored name and a stored enabled flag, and the description of the code.</summary>
    private static readonly RegistryChannel _storedEmail = Channels[0] with { Name = "Shop email", Enabled = false, Entry = _stored };

    [Fact]
    public void Render_ChannelWithNoEntry_ShowsEmptyTextFieldsWithTheValuesOfTheCodeAsPlaceholders()
    {
        var cut = RenderPage("Email");

        Assert.Equal(
            ("", "Email channel", "", "Sends emails", true),
            (ValueOf(cut, "display-name"), PlaceholderOf(cut, "display-name"), ValueOf(cut, "description"), PlaceholderOf(cut, "description"),
                cut.Find("[data-field=enabled]").HasAttribute("checked")));
    }

    [Fact]
    public void Render_ChannelWithAStoredEntry_ShowsTheStoredValuesAndThePlaceholderOfAnEmptyFieldOnlyForAStoredText()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryChannel>>([_storedEmail]));

        var cut = RenderPage("Email");

        Assert.Equal(
            ("Shop email", "An empty field gives the value of the code.", "", "Sends emails", false, "2"),
            (ValueOf(cut, "display-name"), PlaceholderOf(cut, "display-name"), ValueOf(cut, "description"), PlaceholderOf(cut, "description"),
                cut.Find("[data-field=enabled]").HasAttribute("checked"), cut.Find("[data-field=revision]").TextContent));
    }

    [Fact]
    public void Render_KnownChannel_ShowsTheHelpTextUnderTheEnabledFlag()
    {
        var cut = RenderPage("Email");

        var help = cut.Find("[data-field=enabled-help]");
        Assert.Equal(
            ("A change applies to the next events. The outbox still sends the messages that it holds. Another process applies the change within its refresh interval.", 1),
            (help.TextContent, help.PreviousElementSibling!.QuerySelectorAll("[data-field=enabled]").Length));
    }

    [Fact]
    public void ClickSave_EmptyTextFields_SavesNullForTheTextsWithTheRevisionOfTheRead()
    {
        Commands.SaveChannelAsync(Arg.Any<ChannelEntry>(), Arg.Any<CancellationToken>()).Returns(static call => Ok(call.Arg<ChannelEntry>() with { Revision = 3 }));
        var cut = RenderPage("Email");
        cut.Find("[data-field=display-name]").Change(" ");
        cut.Find("[data-field=enabled]").Change(false);

        cut.Find("[data-action=save]").Click();

        Commands.Received(1).SaveChannelAsync(new ChannelEntry(Email, null, null, Enabled: false, Revision: 2, default), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickSave_TypedText_SavesItAndShowsSavedWithTheValuesOfASecondRead()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok(Channels), Ok<IReadOnlyList<RegistryChannel>>([_storedEmail]));
        Commands.SaveChannelAsync(Arg.Any<ChannelEntry>(), Arg.Any<CancellationToken>()).Returns(Ok(_stored));
        var cut = RenderPage("Email");
        cut.Find("[data-field=display-name]").Change("Shop email");

        cut.Find("[data-action=save]").Click();

        Commands.Received(1).SaveChannelAsync(Arg.Is<ChannelEntry>(static entry => entry.DisplayName == "Shop email"), Arg.Any<CancellationToken>());
        Assert.Equal(
            ("Saved.", "Shop email", "2", 1, 0),
            (cut.Find("[data-state=saved]").TextContent, ValueOf(cut, "display-name"), cut.Find("[data-field=revision]").TextContent,
                cut.FindAll("[data-action=reset]").Count, cut.FindAll("[data-state=pending]").Count));
    }

    [Fact]
    public void ClickSave_Conflict_ReadsTheChannelAgainAndShowsTheTextOfConflict()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok(Channels), Ok<IReadOnlyList<RegistryChannel>>([_storedEmail]));
        Commands.SaveChannelAsync(Arg.Any<ChannelEntry>(), Arg.Any<CancellationToken>()).Returns(Fail<ChannelEntry>(Errors.Conflict));
        var cut = RenderPage("Email");
        cut.Find("[data-field=display-name]").Change("My name");

        cut.Find("[data-action=save]").Click();

        Assert.Equal(
            (AdminErrors.ConflictText, "Shop email", 0),
            (cut.Find(".alert-danger").TextContent, ValueOf(cut, "display-name"), cut.FindAll("[data-state=saved]").Count));
    }

    [Fact]
    public void ClickSave_Error_ShowsTheTextOfTheErrorAndKeepsTheFields()
    {
        Commands.SaveChannelAsync(Arg.Any<ChannelEntry>(), Arg.Any<CancellationToken>()).Returns(
            ValueTask.FromResult(ErrorOr<ChannelEntry>.ForError(new Error(Errors.ValidationFailed, "DisplayName has at most 200 characters."))));
        var cut = RenderPage("Email");
        cut.Find("[data-field=display-name]").Change("My name");

        cut.Find("[data-action=save]").Click();

        Assert.Equal(
            ("DisplayName has at most 200 characters.", "My name", 1),
            (cut.Find(".alert-danger").TextContent, ValueOf(cut, "display-name"), Queries.ReceivedCalls().Count()));
    }

    [Fact]
    public void Render_PendingChanges_ShowsTheNamesOfTheChangedFields()
    {
        var cut = RenderPage("Email");

        cut.Find("[data-field=description]").Change("New description");
        cut.Find("[data-field=enabled]").Change(false);

        Assert.Equal("Changes that are not saved: Description, Enabled.", cut.Find("[data-state=pending]").TextContent);
    }

    [Fact]
    public void Render_Actions_HaveBackOnTheLeftAndResetOnlyForAStoredEntryBeforeSaveOnTheRight()
    {
        var withNoEntry = RenderPage("Email");
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryChannel>>([_storedEmail]));
        var withAnEntry = RenderPage("Email");

        Assert.Equal(
            (RegistryAdminRoutes.Channels.TrimStart('/'), "save", "reset save"),
            (withAnEntry.Find("[data-actions-start] [data-action=back]").GetAttribute("href"), ActionsOf(withNoEntry), ActionsOf(withAnEntry)));
    }

    [Fact]
    public void ClickReset_StoredEntry_AsksWithTheStoredValuesAndThatTheChannelIsEnabledAfterTheReset()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryChannel>>([_storedEmail]));
        var cut = RenderPage("Email");

        cut.Find("[data-action=reset]").Click();

        Assert.Equal(
            ("Reset", "Do you want to reset the channel Email?", "Shop email|Value of the code|No|2", "The reset cannot be undone."),
            (cut.Find("[data-dialog] .modal-title").TextContent, cut.Find("[data-dialog] .modal-body > p").TextContent,
                string.Join('|', cut.FindAll("[data-detail]").Select(static detail => detail.TextContent)), cut.Find("[data-delete-warning]").TextContent));
        Assert.Contains("The channel is disabled now, and it is enabled after the reset.", cut.Find("[data-reset-effect]").TextContent, StringComparison.Ordinal);
        Commands.DidNotReceive().ResetChannelAsync(Arg.Any<ChannelId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmReset_StoredEntry_ResetsAndShowsTheValuesOfTheCodeOfASecondRead()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryChannel>>([_storedEmail]), Ok(Channels));
        Commands.ResetChannelAsync(Arg.Any<ChannelId>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(null));
        var cut = RenderPage("Email");
        cut.Find("[data-action=reset]").Click();

        cut.Find("[data-action=confirm-delete]").Click();

        Commands.Received(1).ResetChannelAsync(Email, Arg.Any<CancellationToken>());
        Assert.Equal(
            ("", "Email channel", true, "save", 0),
            (ValueOf(cut, "display-name"), PlaceholderOf(cut, "display-name"), cut.Find("[data-field=enabled]").HasAttribute("checked"), ActionsOf(cut),
                cut.FindAll("[data-dialog]").Count));
    }

    [Fact]
    public void Render_UnknownChannel_ShowsNotFoundAndNoSave()
    {
        var cut = RenderPage("Pager");

        Assert.Equal(("404", ""), (cut.Find(".alert-danger").GetAttribute("data-error-code"), ActionsOf(cut)));
    }

    private IRenderedComponent<ChannelEditPage> RenderPage(string channel) =>
        Render<ChannelEditPage>(parameters => parameters.Add(page => page.ChannelName, channel));

    private static string? ValueOf(IRenderedComponent<ChannelEditPage> cut, string field) => cut.Find($"[data-field={field}]").GetAttribute("value");

    private static string? PlaceholderOf(IRenderedComponent<ChannelEditPage> cut, string field) => cut.Find($"[data-field={field}]").GetAttribute("placeholder");

    private static string ActionsOf(IRenderedComponent<ChannelEditPage> cut) =>
        string.Join(' ', cut.FindAll("[data-actions-end] [data-action]").Select(static action => action.GetAttribute("data-action")));
}
