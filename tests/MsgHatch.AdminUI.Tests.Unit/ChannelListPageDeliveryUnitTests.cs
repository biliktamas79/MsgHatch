using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class ChannelListPageDeliveryUnitTests : RegistryPageContext
{
    [Fact]
    public void Render_DeliveryStatuses_ShowsThePendingCountsAndTheOldestDueTimeOfEachChannel()
    {
        // Arrange
        GivenStatuses(Status(Email, 3, 1, Now.AddMinutes(-5)), Status(Sms, 0, 0, null));

        // Act
        var cut = Render<ChannelListPage>();

        // Assert
        Assert.Equal(
            [("3", "1", "2029-12-31 23:55:00 UTC"), ("0", "0", ""), ("", "", "")],
            cut.FindAll("tr[data-row]").Select(row => (
                row.QuerySelector("[data-cell=pending]")!.TextContent, row.QuerySelector("[data-cell=pending-test]")!.TextContent,
                row.QuerySelector("[data-cell=oldest-due]")!.TextContent)));
    }

    [Fact]
    public void Render_DeliveryUnavailable_ShowsTheErrorAboveTheListAndTheRegistryColumns()
    {
        // Arrange
        DeliveryQueries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<ChannelStatus>>(Errors.Unavailable));

        // Act
        var cut = Render<ChannelListPage>();

        // Assert
        Assert.Equal(AdminErrors.UnavailableText, cut.Find("[data-delivery-error] .alert-danger").TextContent);
        Assert.Equal(["Email", "Sms", "Fax"], cut.FindAll("tr[data-row]").Select(static row => row.Children[0].TextContent));
    }

    [Fact]
    public void Render_UpAndDownChannels_ShowsPauseForAnUpChannelAndResumeForADownChannel()
    {
        // Arrange
        GivenSmsDown();

        // Act
        var cut = Render<ChannelListPage>();

        // Assert
        Assert.Equal(
            ["pause", "resume", "pause"],
            cut.FindAll("tr[data-row]").Select(static row => row.QuerySelector("[data-action=pause], [data-action=resume]")!.GetAttribute("data-action")));
    }

    [Fact]
    public void ClickPause_Email_OpensTheDialogOfEmailAndPausesNothing()
    {
        // Arrange
        var cut = Render<ChannelListPage>();

        // Act
        cut.Find("[data-action=pause]").Click();

        // Assert
        Assert.Equal("Pause Email", cut.Find("[data-dialog] .modal-title").TextContent);
        DeliveryCommands.DidNotReceiveWithAnyArgs().PauseChannelAsync(default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmPause_Note_PausesTheChannelWithTheNoteClosesTheDialogAndReadsTheChannelsAgain()
    {
        // Arrange
        var cut = Render<ChannelListPage>();
        cut.Find("[data-action=pause]").Click();

        // Act
        cut.Find("#msghatch-pause-note").Input(" maintenance ");
        cut.Find("[data-action=confirm-pause]").Click();

        // Assert
        DeliveryCommands.Received(1).PauseChannelAsync(Email, "maintenance", Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetChannelsAsync(Arg.Any<CancellationToken>());
        Queries.Received(2).GetChannelsAsync(Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll("[data-dialog]"));
    }

    [Fact]
    public void ConfirmPause_NoNote_PausesTheChannelWithNoNote()
    {
        // Arrange
        var cut = Render<ChannelListPage>();
        cut.Find("[data-action=pause]").Click();

        // Act
        cut.Find("[data-action=confirm-pause]").Click();

        // Assert
        DeliveryCommands.Received(1).PauseChannelAsync(Email, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmPause_NoteOf501Characters_ShowsValidationFailedInTheDialogAndPausesNothing()
    {
        // Arrange
        var cut = Render<ChannelListPage>();
        cut.Find("[data-action=pause]").Click();

        // Act
        cut.Find("#msghatch-pause-note").Input(new string('n', 501));
        cut.Find("[data-action=confirm-pause]").Click();

        // Assert
        Assert.Equal("400", cut.Find("[data-dialog] .alert-danger").GetAttribute("data-error-code"));
        DeliveryCommands.DidNotReceiveWithAnyArgs().PauseChannelAsync(default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmPause_NoteOf500Characters_PausesTheChannel()
    {
        // Arrange
        var cut = Render<ChannelListPage>();
        cut.Find("[data-action=pause]").Click();

        // Act
        cut.Find("#msghatch-pause-note").Input(new string('n', 500));
        cut.Find("[data-action=confirm-pause]").Click();

        // Assert
        DeliveryCommands.Received(1).PauseChannelAsync(Email, new string('n', 500), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmPause_Rejected_ShowsTheTextOfRejectedInTheDialog()
    {
        // Arrange
        DeliveryCommands.PauseChannelAsync(default, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(new Error(Errors.Rejected)));
        var cut = Render<ChannelListPage>();
        cut.Find("[data-action=pause]").Click();

        // Act
        cut.Find("[data-action=confirm-pause]").Click();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find("[data-dialog] .alert-danger").TextContent);
        DeliveryCommands.Received(1).PauseChannelAsync(Email, null, Arg.Any<CancellationToken>());
        DeliveryQueries.Received(1).GetChannelsAsync(Arg.Any<CancellationToken>());
        Queries.Received(1).GetChannelsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickBackThenPause_NoteInTheFirstDialog_ClosesThePauseAndOpensAnEmptyNote()
    {
        // Arrange
        var cut = Render<ChannelListPage>();
        cut.Find("[data-action=pause]").Click();
        cut.Find("#msghatch-pause-note").Input("maintenance");

        // Act
        cut.Find("[data-dialog] [data-action=back]").Click();
        var closed = cut.FindAll("[data-dialog]").Count;
        cut.Find("[data-action=pause]").Click();

        // Assert
        Assert.Equal((0, string.Empty), (closed, cut.Find("#msghatch-pause-note").GetAttribute("value") ?? string.Empty));
        DeliveryCommands.DidNotReceiveWithAnyArgs().PauseChannelAsync(default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickResume_DownChannel_ResumesTheChannelAndReadsTheChannelsAgain()
    {
        // Arrange
        GivenSmsDown();
        var cut = Render<ChannelListPage>();

        // Act
        cut.Find("[data-action=resume]").Click();

        // Assert
        DeliveryCommands.Received(1).ResumeChannelAsync(Sms, Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetChannelsAsync(Arg.Any<CancellationToken>());
        Queries.Received(2).GetChannelsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickResume_Rejected_ShowsTheTextOfRejectedAboveTheList()
    {
        // Arrange
        GivenSmsDown();
        DeliveryCommands.ResumeChannelAsync(default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(new Error(Errors.Rejected)));
        var cut = Render<ChannelListPage>();

        // Act
        cut.Find("[data-action=resume]").Click();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find("[data-action-error] .alert-danger").TextContent);
        DeliveryCommands.Received(1).ResumeChannelAsync(Sms, Arg.Any<CancellationToken>());
    }

    private static ChannelStatus Status(ChannelId channel, int pending, int pendingTest, DateTime? oldestDue) =>
        new(new ChannelDescriptor<TestChannelSettings>(channel, channel.Name, ChannelFeatures.None), null, pending, pendingTest, oldestDue);

    private void GivenStatuses(params ChannelStatus[] statuses) =>
        DeliveryQueries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<ChannelStatus>>(statuses));

    private void GivenSmsDown() =>
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryChannel>>(
        [
            Channels[0],
            Channels[1] with { Current = new(Sms, 4, ChannelStateKind.Down, Now.AddMinutes(-5), null, null, ChannelDownReason.Operator, null, null, null, "maintenance") },
            Channels[2],
        ]));

    private sealed record TestChannelSettings : ChannelSettings;
}
