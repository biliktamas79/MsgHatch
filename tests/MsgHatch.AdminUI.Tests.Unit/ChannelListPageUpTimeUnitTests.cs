using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class ChannelListPageUpTimeUnitTests : RegistryPageContext
{
    [Fact]
    public void Render_NoStateRows_ShowsAnUpTimeOf100PercentForEachWindow()
    {
        // Act
        var cut = Render<ChannelListPage>();

        // Assert
        Assert.Equal(
            ["1 day: 100 %", "1 week: 100 %", "30 days: 100 %"],
            cut.FindAll("tr[data-row]")[0].QuerySelectorAll("[data-up-time]").Select(static window => window.TextContent));
        Queries.Received(1).GetChannelHistoryAsync(Email, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_DownStates_ShowsTheUpTimeOfEachWindowCutDownToTwoDecimals()
    {
        // Arrange
        GivenHistory(Email, Up(Now.AddMinutes(-144)), Down(Now.AddHours(-3)), Up(Now.AddHours(-36)), Down(Now.AddDays(-2)), Up(Now.AddDays(-40).AddHours(1)), Down(Now.AddDays(-40)));
        GivenHistory(Sms, Down(Now.AddDays(-2)));
        GivenHistory(Fax, Up(Now.AddDays(-10).AddMinutes(1)), Down(Now.AddDays(-10)));

        // Act
        var cut = Render<ChannelListPage>();

        // Assert
        Assert.Equal(
            [
                ["1 day: 97.5 %", "1 week: 92.5 %", "30 days: 98.25 %"],
                ["1 day: 0 %", "1 week: 71.42 %", "30 days: 93.33 %"],
                ["1 day: 100 %", "1 week: 100 %", "30 days: 99.99 %"],
            ],
            cut.FindAll("tr[data-row]").Select(static row => row.QuerySelectorAll("[data-up-time]").Select(static window => window.TextContent).ToArray()));
    }

    [Fact]
    public void Render_HistoryOfSmsRejected_ShowsTheErrorAboveTheListAndNoUpTimeOfSms()
    {
        // Arrange
        Queries.GetChannelHistoryAsync(Sms, Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<ChannelState>>(Errors.Rejected));

        // Act
        var cut = Render<ChannelListPage>();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find("[data-history-error] .alert-danger").TextContent);
        Assert.Equal([3, 0, 3], cut.FindAll("tr[data-row]").Select(static row => row.QuerySelectorAll("[data-up-time]").Length));
        Queries.Received(1).GetChannelHistoryAsync(Email, Arg.Any<CancellationToken>());
        Queries.Received(1).GetChannelHistoryAsync(Sms, Arg.Any<CancellationToken>());
        Queries.Received(1).GetChannelHistoryAsync(Fax, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickCheck_Channel_ReadsTheHistoryOfEachChannelAgain()
    {
        // Arrange
        var cut = Render<ChannelListPage>();

        // Act
        cut.Find("[data-action=check]").Click();

        // Assert
        Commands.Received(1).CheckChannelAsync(Email, Arg.Any<CancellationToken>());
        Queries.Received(2).GetChannelHistoryAsync(Email, Arg.Any<CancellationToken>());
        Queries.Received(2).GetChannelHistoryAsync(Sms, Arg.Any<CancellationToken>());
        Queries.Received(2).GetChannelHistoryAsync(Fax, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Channels_PutsPauseHistoryAndCheckAboveViewAndEdit()
    {
        // Act
        var cut = Render<ChannelListPage>();

        // Assert
        Assert.Equal(
            ["pause history check", "view edit"],
            cut.FindAll("tr[data-row]")[0].QuerySelectorAll("[data-action-row]")
                .Select(static row => string.Join(' ', row.Children.Select(static button => button.GetAttribute("data-action")))));
    }

    private static ChannelState Up(DateTime sinceUtc) => new(Email, 0, ChannelStateKind.Up, sinceUtc, null, null, null, null, null, null, null);

    private static ChannelState Down(DateTime sinceUtc) =>
        new(Email, 0, ChannelStateKind.Down, sinceUtc, null, null, ChannelDownReason.Operator, null, null, null, null);

    private void GivenHistory(ChannelId channel, params ChannelState[] newestFirst) =>
        Queries.GetChannelHistoryAsync(channel, Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<ChannelState>>(newestFirst));
}
