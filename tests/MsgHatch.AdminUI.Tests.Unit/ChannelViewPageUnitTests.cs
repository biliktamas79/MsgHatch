using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class ChannelViewPageUnitTests : RegistryPageContext
{
    [Fact]
    public void Render_KnownChannel_ShowsItsFieldsWithTheFeaturesAsBadges()
    {
        var cut = RenderPage("Email");

        Assert.Equal(
            ("Email", "Email channel", "Sends emails", "Yes", "2", "Languages,RichText"),
            (Field(cut, "id"), Field(cut, "name"), Field(cut, "description"), Field(cut, "enabled"), Field(cut, "revision"),
                string.Join(',', cut.FindAll("[data-field=features] .badge").Select(static badge => badge.TextContent))));
    }

    [Fact]
    public void Render_PausedChannel_ShowsTheFieldsStateAndSince()
    {
        var paused = new ChannelState(Email, 1, ChannelStateKind.Down, Now.AddDays(-2), null, null, ChannelDownReason.Operator, null, null, null, "maintenance");
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryChannel>>([Channels[0] with { Current = paused }]));

        var cut = RenderPage("Email");

        Assert.Equal(
            ("Down Paused maintenance", "2029-12-30 00:00 UTC (2 d 0 h)"),
            (SpacedTextOf(cut.Find("[data-field=state]")), SpacedTextOf(cut.Find("[data-field=since]"))));
    }

    [Fact]
    public void ClickHistory_KnownChannel_OpensTheDialogOfTheChannel()
    {
        var cut = RenderPage("Email");

        cut.Find("[data-actions-end] [data-action=history]").Click();

        Assert.Equal("History of Email", cut.Find("[data-dialog] .modal-title").TextContent);
        Queries.Received(1).GetChannelHistoryAsync(Email, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_KnownChannel_HasEditAsTheMainActionOnTheRightAfterHistory()
    {
        var cut = RenderPage("Email");

        var actions = cut.FindAll("[data-actions-end] > *");
        Assert.Equal(
            ("history edit", "btn btn-primary", RegistryAdminRoutes.ChannelEdit(Email).TrimStart('/')),
            (string.Join(' ', actions.Select(static action => action.GetAttribute("data-action"))), actions[^1].ClassName, actions[^1].GetAttribute("href")));
    }

    [Fact]
    public void Render_MessageTypesOfTheChannel_ShowEachWithItsDisplayNameAndItsStateOnTheChannel()
    {
        var cut = RenderPage("Email");

        Assert.Equal(
            ["Requests|Requests of a client|Enabled", "Notices||Message type disabled", "Invoices|Invoices|Disabled on this channel"],
            cut.FindAll("tr[data-row]").Select(static row => string.Join('|', row.Children.Select(static cell => cell.TextContent))));
    }

    [Fact]
    public void Render_ChannelWithNoMessageType_ShowsTheEmptyText()
    {
        var cut = RenderPage("Fax");

        Assert.Equal("No message type registers this channel.", cut.Find("[data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_UnknownChannel_ShowsNotFoundAndTheLinkBack()
    {
        var cut = RenderPage("Pager");

        Assert.Equal(
            ("404", RegistryAdminRoutes.Channels.TrimStart('/'), 0),
            (cut.Find(".alert-danger").GetAttribute("data-error-code"), cut.Find("[data-action=back]").GetAttribute("href"), cut.FindAll("dl").Count));
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailableAndHasNoMainAction()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<RegistryMessageType>>(Errors.Unavailable));

        var cut = RenderPage("Email");

        Assert.Equal((AdminErrors.UnavailableText, 0), (cut.Find(".alert-danger").TextContent, cut.FindAll("[data-actions-end] > *").Count));
    }

    private IRenderedComponent<ChannelViewPage> RenderPage(string channel) =>
        Render<ChannelViewPage>(parameters => parameters.Add(page => page.ChannelName, channel));

    private static string Field(IRenderedComponent<ChannelViewPage> cut, string name) => cut.Find($"[data-field={name}]").TextContent.Trim();
}
