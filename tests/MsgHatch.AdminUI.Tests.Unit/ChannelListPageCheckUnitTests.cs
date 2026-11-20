using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

/// <summary>The button Check of the channel list and the alert of its result (D283).</summary>
public sealed class ChannelListPageCheckUnitTests : RegistryPageContext
{
    [Fact]
    public void Render_Channels_ShowsCheckBetweenHistoryAndViewInEachRow()
    {
        var cut = Render<ChannelListPage>();

        Assert.Equal(
            ["history check view edit", "history check view edit", "history check view edit"],
            cut.FindAll("tr[data-row]").Select(static row => string.Join(' ', row.QuerySelectorAll("[data-action]").Select(static action => action.GetAttribute("data-action")))));
    }

    [Theory]
    [InlineData(null, null, true, "Sms: healthy. The channel is up again.", "alert-success")]
    [InlineData(null, null, false, "Sms: healthy. The state did not change.", "alert-success")]
    [InlineData(503, "Unavailable", true, "Sms: Unavailable. The channel is down now.", "alert-warning")]
    [InlineData(503, "Unavailable", false, "Sms: Unavailable. The state did not change.", "alert-warning")]
    public void ClickCheck_SecondChannel_ChecksThatChannelAndShowsTheTextAndTheClassOfTheResult(
        int? errorCode, string? errorName, bool changed, string expectedText, string expectedClass)
    {
        Commands.CheckChannelAsync(Sms, Arg.Any<CancellationToken>()).Returns(Ok(new ChannelCheck(Sms, errorCode, errorName, changed)));
        var cut = Render<ChannelListPage>();

        cut.FindAll("[data-action=check]")[1].Click();

        var alert = cut.Find("[data-check-result]");
        Assert.Equal((expectedText, true), (alert.TextContent, alert.ClassList.Contains(expectedClass)));
    }

    [Fact]
    public void ClickCheck_ErrorOfTheCommand_ShowsTheTextOfTheErrorWithAlertDanger()
    {
        Commands.CheckChannelAsync(Email, Arg.Any<CancellationToken>()).Returns(
            ValueTask.FromResult(ErrorOr<ChannelCheck>.ForError(new Error(Errors.Rejected, "The token has no role RegistryAdmin."))));
        var cut = Render<ChannelListPage>();

        cut.FindAll("[data-action=check]")[0].Click();

        var alert = cut.Find("[data-check-result]");
        Assert.Equal((AdminErrors.RejectedText, true), (alert.TextContent, alert.ClassList.Contains("alert-danger")));
    }

    [Fact]
    public void ClickCheck_Result_ReadsTheChannelsAgain()
    {
        Commands.CheckChannelAsync(Email, Arg.Any<CancellationToken>()).Returns(Ok(new ChannelCheck(Email, 503, "Unavailable", Changed: true)));
        var cut = Render<ChannelListPage>();

        cut.FindAll("[data-action=check]")[0].Click();

        Queries.Received(2).GetChannelsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickCheck_RunningCheck_DisablesTheCheckButtonsUntilTheResult()
    {
        var result = new TaskCompletionSource<ErrorOr<ChannelCheck>>();
        Commands.CheckChannelAsync(Fax, Arg.Any<CancellationToken>()).Returns(new ValueTask<ErrorOr<ChannelCheck>>(result.Task));
        var cut = Render<ChannelListPage>();

        cut.FindAll("[data-action=check]")[2].Click();
        var whileRunning = DisabledFlagsOf(cut);
        result.SetResult(ErrorOr<ChannelCheck>.ForValue(new ChannelCheck(Fax, ErrorCode: null, ErrorName: null, Changed: false)));

        cut.WaitForAssertion(() => Assert.Equal(("True True True", "False False False"), (whileRunning, DisabledFlagsOf(cut))));
    }

    [Fact]
    public void Render_NoCheck_ShowsNoResult()
    {
        var cut = Render<ChannelListPage>();

        Assert.Empty(cut.FindAll("[data-check-result]"));
        Commands.DidNotReceiveWithAnyArgs().CheckChannelAsync(default, Xunit.TestContext.Current.CancellationToken);
    }

    private static string DisabledFlagsOf(IRenderedComponent<ChannelListPage> cut) =>
        string.Join(' ', cut.FindAll("[data-action=check]").Select(static button => button.HasAttribute("disabled")));
}
