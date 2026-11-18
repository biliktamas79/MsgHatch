using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class ChannelHistoryDialogUnitTests : RegistryPageContext
{
    [Fact]
    public void Render_KnownRows_ShowsTheWidthsAndColorsOfTheSegmentsOldestFirst()
    {
        GivesKnownHistory();

        var cut = RenderDialog(Channels[0]);

        Assert.Equal(
            [("up", "width: 50%", "bg-success"), ("down", "width: 10%", "bg-danger"), ("up", "width: 30%", "bg-success"), ("down", "width: 10%", "bg-danger")],
            cut.FindAll("[data-timeline] > [data-segment]").Select(static segment => (
                segment.GetAttribute("data-segment"), segment.GetAttribute("style"), segment.QuerySelector(".progress-bar")!.ClassList[1])));
    }

    [Fact]
    public void Render_KnownRows_ShowsARowForEachStateNewestFirstWithThePreviousState()
    {
        GivesKnownHistory();

        var cut = RenderDialog(Channels[0]);

        Assert.Equal(
            [
                "2029-12-29 00:00 UTC|Down|Paused||maintenance|Up for 9 d 0 h",
                "2029-12-20 00:00 UTC|Up||||Down for 3 d 0 h",
                "2029-12-17 00:00 UTC|Down|Errors|Unavailable||Up for 24 d 0 h",
                "2029-11-23 00:00 UTC|Up||||Down for 1 d 0 h",
            ],
            cut.FindAll("[data-dialog] tr[data-row]").Select(static row => string.Join('|', row.Children.Select(static cell => cell.TextContent))));
    }

    [Theory]
    [InlineData(ChannelStateKind.Down, "down")]
    [InlineData(ChannelStateKind.Up, "up")]
    public void Render_OnlyTheRowBeforeTheWindow_FillsTheBarWithItsState(ChannelStateKind kind, string expected)
    {
        GivesHistory(ARow(2, kind, -40));

        var cut = RenderDialog(Channels[0]);

        Assert.Equal(
            [(expected, "width: 100%")],
            cut.FindAll("[data-segment]").Select(static segment => (segment.GetAttribute("data-segment"), segment.GetAttribute("style"))));
    }

    [Fact]
    public void Render_NoRow_FillsTheBarWithUpAndShowsTheEmptyText()
    {
        var cut = RenderDialog(Channels[0]);

        Assert.Equal(
            [("up", "width: 100%")],
            cut.FindAll("[data-segment]").Select(static segment => (segment.GetAttribute("data-segment"), segment.GetAttribute("style"))));
        Assert.Equal("No down state in 30 days.", cut.Find("[data-dialog] [data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_DownRowBeforeTheWindowAndAnUpRowInIt_StartsTheBarDown()
    {
        GivesHistory(ARow(2, ChannelStateKind.Up, -10), ARow(1, ChannelStateKind.Down, -35));

        var cut = RenderDialog(Channels[0]);

        Assert.Equal(
            [("down", "width: 66.667%"), ("up", "width: 33.333%")],
            cut.FindAll("[data-segment]").Select(static segment => (segment.GetAttribute("data-segment"), segment.GetAttribute("style"))));
    }

    [Fact]
    public void Render_HistoryUnavailable_ShowsTheTextOfTheErrorAndNoBar()
    {
        Queries.GetChannelHistoryAsync(Email, Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<ChannelState>>(Errors.Unavailable));

        var cut = RenderDialog(Channels[0]);

        Assert.Equal((AdminErrors.UnavailableText, 0), (cut.Find("[data-dialog] .alert-danger").TextContent, cut.FindAll("[data-timeline]").Count));
    }

    [Fact]
    public void ClickClose_OpenDialog_IsOnTheLeftAndCallsOnClose()
    {
        var closed = 0;
        var cut = Render<ChannelHistoryDialog>(parameters => parameters
            .Add(static dialog => dialog.Channel, Channels[0])
            .Add(static dialog => dialog.OnClose, () => closed++));

        cut.Find("[data-actions-start] [data-action=close]").Click();

        Assert.Equal((1, 0), (closed, cut.FindAll("[data-actions-end] > *").Count));
    }

    [Fact]
    public void Render_NoChannel_ShowsNoDialogAndReadsNoHistory()
    {
        var cut = Render<ChannelHistoryDialog>();

        Assert.Empty(cut.FindAll("[data-dialog]"));
        Queries.DidNotReceive().GetChannelHistoryAsync(Arg.Any<ChannelId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The rows of the window and the row before it: up from 39 days ago, down for errors 15 days ago, up 12 days ago, and
    /// paused 3 days ago.
    /// </summary>
    private void GivesKnownHistory() =>
        GivesHistory(
            ARow(5, ChannelStateKind.Down, -3, ChannelDownReason.Operator, ChannelStateKind.Up, 9) with { Note = "maintenance" },
            ARow(4, ChannelStateKind.Up, -12, previousKind: ChannelStateKind.Down, previousDays: 3),
            ARow(3, ChannelStateKind.Down, -15, ChannelDownReason.Errors, ChannelStateKind.Up, 24) with { ErrorCode = 503, ErrorName = "Unavailable" },
            ARow(2, ChannelStateKind.Up, -39, previousKind: ChannelStateKind.Down, previousDays: 1));

    private ChannelState ARow(
        int version, ChannelStateKind kind, int days, ChannelDownReason? reason = null, ChannelStateKind? previousKind = null, int? previousDays = null) =>
        new(
            Email, version, kind, Now.AddDays(days), previousKind, previousDays is { } previous ? TimeSpan.FromDays(previous) : null,
            kind == ChannelStateKind.Down ? reason ?? ChannelDownReason.Operator : null, null, null, null, null);

    private void GivesHistory(params ChannelState[] newestFirst) =>
        Queries.GetChannelHistoryAsync(Email, Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<ChannelState>>(newestFirst));

    private IRenderedComponent<ChannelHistoryDialog> RenderDialog(RegistryChannel channel) =>
        Render<ChannelHistoryDialog>(parameters => parameters.Add(static dialog => dialog.Channel, channel));
}
