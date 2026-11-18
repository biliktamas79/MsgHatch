using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class ChannelListPageUnitTests : RegistryPageContext
{
    [Fact]
    public void Render_Channels_ShowsEachRowWithItsFeaturesItsEnabledFlagItsMessageTypeCountAndTheAddressOfView()
    {
        var cut = Render<ChannelListPage>();

        var rows = cut.FindAll("tr[data-row]");
        Assert.Equal(
            ["Email", "Email channel", "LanguagesRichText", "Yes", "3", "Up", "No down state recorded", "History Check View"], rows[0].Children.Select(SpacedTextOf));
        Assert.Equal(["Sms", "Text messages", "", "No", "1", "Up", "No down state recorded", "History Check View"], rows[1].Children.Select(SpacedTextOf));
        Assert.Equal(["Fax", "Fax", "GroupDelivery", "Yes", "0", "Up", "No down state recorded", "History Check View"], rows[2].Children.Select(SpacedTextOf));
        Assert.Equal(
            ("Channels", 2, RegistryAdminRoutes.Channel(Email).TrimStart('/')),
            (cut.Find("[data-list-header] h1").TextContent, rows[0].QuerySelectorAll("[data-feature].badge").Length,
                rows[0].QuerySelector("[data-action=view]")?.GetAttribute("href")));
    }

    [Fact]
    public void Render_ChannelStates_ShowsTheBadgeTheReasonAndTheSinceTextOfEachChannel()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryChannel>>(
        [
            Channels[0] with { Current = new(Email, 1, ChannelStateKind.Down, Now.AddHours(-76), null, null, ChannelDownReason.Errors, Now, 503, "Unavailable", null) },
            Channels[1] with
            {
                Current = new(
                    Sms, 4, ChannelStateKind.Down, Now.AddMinutes(-125), ChannelStateKind.Up, TimeSpan.FromHours(2), ChannelDownReason.Operator, null, null, null,
                    "maintenance"),
            },
            Channels[2] with { Current = new(Fax, 2, ChannelStateKind.Up, Now.AddMinutes(-12), ChannelStateKind.Down, TimeSpan.FromMinutes(4), null, null, null, null, null) },
        ]));

        var cut = Render<ChannelListPage>();

        Assert.Equal(
            [
                ("Down Unavailable", "text-bg-danger", "2029-12-28 20:00 UTC (3 d 4 h)"),
                ("Down Paused maintenance", "text-bg-danger", "2029-12-31 21:55 UTC (2 h 5 min), after Up for 2 h 0 min"),
                ("Up", "text-bg-success", "2029-12-31 23:48 UTC (12 min), after Down for 4 min"),
            ],
            cut.FindAll("tr[data-row]").Select(row => (
                SpacedTextOf(row.QuerySelector("[data-cell=state]")!), row.QuerySelector("[data-cell=state] .badge")!.ClassList[1],
                SpacedTextOf(row.QuerySelector("[data-cell=since]")!))));
    }

    [Fact]
    public void ClickHistory_SecondChannel_OpensTheDialogWithTheHistoryOfThatChannel()
    {
        var cut = Render<ChannelListPage>();
        Assert.Empty(cut.FindAll("[data-dialog]"));

        cut.FindAll("[data-action=history]")[1].Click();

        Assert.Equal("History of Sms", cut.Find("[data-dialog] .modal-title").TextContent);
        Queries.Received(1).GetChannelHistoryAsync(Sms, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickClose_OpenHistory_ClosesTheDialog()
    {
        var cut = Render<ChannelListPage>();
        cut.FindAll("[data-action=history]")[0].Click();

        cut.Find("[data-dialog] [data-action=close]").Click();

        Assert.Empty(cut.FindAll("[data-dialog]"));
    }

    [Fact]
    public void Render_NoChannel_ShowsTheEmptyText()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryChannel>>([]));

        var cut = Render<ChannelListPage>();

        Assert.Equal("There are no channels.", cut.Find("[data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_ChannelsUnavailable_ShowsTheTextOfUnavailable()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<RegistryChannel>>(Errors.Unavailable));

        var cut = Render<ChannelListPage>();

        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_MessageTypesRejected_ShowsTheTextOfRejected()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<RegistryMessageType>>(Errors.Rejected));

        var cut = Render<ChannelListPage>();

        Assert.Equal((AdminErrors.RejectedText, 0), (cut.Find(".alert-danger").TextContent, cut.FindAll("tr[data-row]").Count));
    }
}
