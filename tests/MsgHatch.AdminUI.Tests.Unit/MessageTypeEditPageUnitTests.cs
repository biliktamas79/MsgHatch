using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

/// <summary>The edit page of the stored settings of one message type (D284, D287), on the message types of <see cref="RegistryPageContext"/>.</summary>
public sealed class MessageTypeEditPageUnitTests : RegistryPageContext
{
    private static readonly MessageType _requests = new("Requests");

    private static readonly MessageTypeEntry _stored =
        new(_requests, null, "Stored description", Enabled: false, [Sms], Revision: 4, new DateTime(2029, 12, 31, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>The message type Requests with a stored description, a stored enabled flag, and the channel Sms disabled.</summary>
    private static readonly RegistryMessageType _storedRequests =
        MessageTypes[0] with { Description = "Stored description", Enabled = false, DisabledChannels = [Sms], Revision = 4, Entry = _stored };

    [Fact]
    public void Render_MessageTypeWithNoEntry_ShowsEmptyTextFieldsTheValuesOfTheCodeAsPlaceholdersAndACheckedBoxForEachChannel()
    {
        var cut = RenderPage("Requests");

        Assert.Equal(
            ("", "Requests of a client", "", "", "Email:True Sms:True", "Send on Email|Send on Sms"),
            (ValueOf(cut, "display-name"), PlaceholderOf(cut, "display-name"), ValueOf(cut, "description"), PlaceholderOf(cut, "description"),
                BoxesOf(cut), string.Join('|', cut.FindAll("[data-field=channels] label").Select(static label => label.TextContent))));
    }

    [Fact]
    public void Render_StoredEntryWithADisabledChannel_ShowsTheStoredValuesAndAClearedBoxForTheChannel()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryMessageType>>([_storedRequests]));

        var cut = RenderPage("Requests");

        Assert.Equal(
            ("", "Requests of a client", "Stored description", "An empty field gives the value of the code.", false, "Email:True Sms:False", "4"),
            (ValueOf(cut, "display-name"), PlaceholderOf(cut, "display-name"), ValueOf(cut, "description"), PlaceholderOf(cut, "description"),
                cut.Find("[data-field=enabled]").HasAttribute("checked"), BoxesOf(cut), cut.Find("[data-field=revision]").TextContent));
    }

    [Fact]
    public void ClickSave_ClearedBox_SavesItsChannelInDisabledChannelsAndNullForTheEmptyTexts()
    {
        Commands.SaveMessageTypeAsync(Arg.Any<MessageTypeEntry>(), Arg.Any<CancellationToken>()).Returns(Ok(_stored));
        var cut = RenderPage("Requests");
        cut.Find("[data-send-on=Sms]").Change(false);

        cut.Find("[data-action=save]").Click();

        var entry = Assert.Single(Commands.ReceivedCalls()).GetArguments()[0] as MessageTypeEntry;
        Assert.Equal(
            ("Requests", null, null, true, "Sms", 0),
            (entry?.Type.Name, entry?.DisplayName, entry?.Description, entry?.Enabled, string.Join(',', entry!.DisabledChannels.Select(static channel => channel.Name)),
                entry.Revision));
    }

    [Fact]
    public void ClickSave_CheckedBoxOfADisabledChannel_SavesNoDisabledChannelAndShowsSavedWithTheValuesOfASecondRead()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(
            Ok<IReadOnlyList<RegistryMessageType>>([_storedRequests]),
            Ok<IReadOnlyList<RegistryMessageType>>([_storedRequests with { DisabledChannels = [], Revision = 5, Entry = _stored with { DisabledChannels = [], Revision = 5 } }]));
        Commands.SaveMessageTypeAsync(Arg.Any<MessageTypeEntry>(), Arg.Any<CancellationToken>()).Returns(Ok(_stored with { DisabledChannels = [], Revision = 5 }));
        var cut = RenderPage("Requests");
        cut.Find("[data-send-on=Sms]").Change(true);

        cut.Find("[data-action=save]").Click();

        Commands.Received(1).SaveMessageTypeAsync(
            Arg.Is<MessageTypeEntry>(static entry => entry.DisabledChannels.Count == 0 && entry.Revision == 4), Arg.Any<CancellationToken>());
        Assert.Equal(
            ("Saved.", "Email:True Sms:True", "5", 0),
            (cut.Find("[data-state=saved]").TextContent, BoxesOf(cut), cut.Find("[data-field=revision]").TextContent, cut.FindAll("[data-state=pending]").Count));
    }

    [Fact]
    public void ClickSave_Conflict_ReadsTheMessageTypeAgainAndShowsTheTextOfConflict()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok(MessageTypes), Ok<IReadOnlyList<RegistryMessageType>>([_storedRequests]));
        Commands.SaveMessageTypeAsync(Arg.Any<MessageTypeEntry>(), Arg.Any<CancellationToken>()).Returns(Fail<MessageTypeEntry>(Errors.Conflict));
        var cut = RenderPage("Requests");
        cut.Find("[data-send-on=Email]").Change(false);

        cut.Find("[data-action=save]").Click();

        Assert.Equal(
            (AdminErrors.ConflictText, "Email:True Sms:False", 0),
            (cut.Find("[data-actions-middle] .alert-danger").TextContent, BoxesOf(cut), cut.FindAll("[data-state=saved]").Count));
    }

    [Fact]
    public void ClickSave_Error_ShowsTheTextOfTheErrorAndKeepsTheFields()
    {
        Commands.SaveMessageTypeAsync(Arg.Any<MessageTypeEntry>(), Arg.Any<CancellationToken>()).Returns(
            ValueTask.FromResult(ErrorOr<MessageTypeEntry>.ForError(new Error(Errors.ValidationFailed, "DisabledChannels has only the channels of the message type Requests."))));
        var cut = RenderPage("Requests");
        cut.Find("[data-send-on=Email]").Change(false);

        cut.Find("[data-action=save]").Click();

        Assert.Equal(
            ("DisabledChannels has only the channels of the message type Requests.", "Email:False Sms:True", 1),
            (cut.Find("[data-actions-middle] .alert-danger").TextContent, BoxesOf(cut), Queries.ReceivedCalls().Count()));
    }

    [Fact]
    public void Render_PendingChanges_ShowsTheNamesOfTheChangedFieldsAndBoxes()
    {
        var cut = RenderPage("Requests");

        cut.Find("[data-field=display-name]").Change("Client requests");
        cut.Find("[data-field=description]").Change("Sent when a client asks");
        cut.Find("[data-field=enabled]").Change(false);
        cut.Find("[data-send-on=Sms]").Change(false);

        Assert.Equal("Changes that are not saved: Display name, Description, Enabled, Send on Sms.", cut.Find("[data-state=pending]").TextContent);
    }

    [Fact]
    public void Render_Actions_HaveBackOnTheLeftAndResetOnlyForAStoredEntryBeforeSaveOnTheRight()
    {
        var withNoEntry = RenderPage("Requests");
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryMessageType>>([_storedRequests]));
        var withAnEntry = RenderPage("Requests");

        Assert.Equal(
            (RegistryAdminRoutes.MessageTypes.TrimStart('/'), "save", "reset save"),
            (withAnEntry.Find("[data-actions-start] [data-action=back]").GetAttribute("href"), ActionsOf(withNoEntry), ActionsOf(withAnEntry)));
    }

    [Fact]
    public void ClickReset_StoredEntry_AsksWithTheStoredValuesAndThatTheMessageTypeIsEnabledAfterTheReset()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryMessageType>>([_storedRequests]));
        var cut = RenderPage("Requests");

        cut.Find("[data-action=reset]").Click();

        Assert.Equal(
            ("Reset", "Do you want to reset the message type Requests?", "Value of the code|Stored description|No|Sms|4"),
            (cut.Find("[data-dialog] .modal-title").TextContent, cut.Find("[data-dialog] .modal-body > p").TextContent,
                string.Join('|', cut.FindAll("[data-detail]").Select(static detail => detail.TextContent))));
        Assert.Contains("The message type is disabled now, and it is enabled after the reset.", cut.Find("[data-reset-effect]").TextContent, StringComparison.Ordinal);
        Commands.DidNotReceive().ResetMessageTypeAsync(Arg.Any<MessageType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmReset_StoredEntry_ResetsAndShowsTheValuesOfTheCodeOfASecondRead()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryMessageType>>([_storedRequests]), Ok(MessageTypes));
        Commands.ResetMessageTypeAsync(Arg.Any<MessageType>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(null));
        var cut = RenderPage("Requests");
        cut.Find("[data-action=reset]").Click();

        cut.Find("[data-action=confirm-delete]").Click();

        Commands.Received(1).ResetMessageTypeAsync(_requests, Arg.Any<CancellationToken>());
        Assert.Equal(
            ("", "Email:True Sms:True", true, "save", 0),
            (ValueOf(cut, "description"), BoxesOf(cut), cut.Find("[data-field=enabled]").HasAttribute("checked"), ActionsOf(cut), cut.FindAll("[data-dialog]").Count));
    }

    [Fact]
    public void Render_UnknownMessageType_ShowsNotFoundAndNoSave()
    {
        var cut = RenderPage("Payments");

        Assert.Equal(("404", ""), (cut.Find(".alert-danger").GetAttribute("data-error-code"), ActionsOf(cut)));
    }

    private IRenderedComponent<MessageTypeEditPage> RenderPage(string type) =>
        Render<MessageTypeEditPage>(parameters => parameters.Add(page => page.TypeName, type));

    private static string? ValueOf(IRenderedComponent<MessageTypeEditPage> cut, string field) => cut.Find($"[data-field={field}]").GetAttribute("value");

    private static string? PlaceholderOf(IRenderedComponent<MessageTypeEditPage> cut, string field) =>
        cut.Find($"[data-field={field}]").GetAttribute("placeholder");

    /// <summary>The check box of each channel as text: the channel and true for a checked box.</summary>
    private static string BoxesOf(IRenderedComponent<MessageTypeEditPage> cut) =>
        string.Join(' ', cut.FindAll("[data-send-on]").Select(static box => $"{box.GetAttribute("data-send-on")}:{box.HasAttribute("checked")}"));

    private static string ActionsOf(IRenderedComponent<MessageTypeEditPage> cut) =>
        string.Join(' ', cut.FindAll("[data-actions-end] [data-action]").Select(static action => action.GetAttribute("data-action")));
}
