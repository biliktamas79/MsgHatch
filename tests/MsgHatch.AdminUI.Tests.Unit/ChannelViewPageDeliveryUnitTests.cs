using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class ChannelViewPageDeliveryUnitTests : RegistryPageContext
{
    [Fact]
    public void Render_DeliveryStatus_ShowsThePendingCountsAndTheOldestDueTime()
    {
        // Arrange
        DeliveryQueries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<ChannelStatus>>(
            [Status(Sms, 7, 2, null), Status(Email, 3, 1, Now.AddMinutes(-5))]));

        // Act
        var cut = RenderPage("Email");

        // Assert
        Assert.Equal(
            ("3", "1", "2029-12-31 23:55:00 UTC"),
            (cut.Find("[data-field=pending]").TextContent, cut.Find("[data-field=pending-test]").TextContent, cut.Find("[data-field=oldest-due]").TextContent));
    }

    [Fact]
    public void Render_DeliveryRejected_ShowsTheErrorAndTheFieldsOfTheRegistry()
    {
        // Arrange
        DeliveryQueries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<ChannelStatus>>(Errors.Rejected));

        // Act
        var cut = RenderPage("Email");

        // Assert
        Assert.Equal(
            (AdminErrors.RejectedText, "Email channel", string.Empty),
            (cut.Find("[data-delivery-error] .alert-danger").TextContent, cut.Find("[data-field=name]").TextContent, cut.Find("[data-field=pending]").TextContent));
    }

    [Fact]
    public void ConfirmPause_UpChannel_PausesTheChannelAndReadsTheChannelAgain()
    {
        // Arrange
        const string note = "maintenance";
        var cut = RenderPage("Email");
        cut.Find("[data-action=pause]").Click();

        // Act
        cut.Find("#msghatch-pause-note").Input(note);
        cut.Find("[data-action=confirm-pause]").Click();

        // Assert
        DeliveryCommands.Received(1).PauseChannelAsync(Email, note, Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetChannelsAsync(Arg.Any<CancellationToken>());
        Queries.Received(2).GetChannelsAsync(Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll("[data-dialog]"));
    }

    [Fact]
    public void Render_DownChannel_HasResumeAndNoPause()
    {
        // Arrange
        GivenEmailDown();

        // Act
        var cut = RenderPage("Email");

        // Assert
        Assert.Equal((1, 0), (cut.FindAll("[data-action=resume]").Count, cut.FindAll("[data-action=pause]").Count));
    }

    [Fact]
    public void ClickResume_DownChannel_ResumesTheChannelAndReadsTheChannelAgain()
    {
        // Arrange
        GivenEmailDown();
        var cut = RenderPage("Email");

        // Act
        cut.Find("[data-action=resume]").Click();

        // Assert
        DeliveryCommands.Received(1).ResumeChannelAsync(Email, Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetChannelsAsync(Arg.Any<CancellationToken>());
        Queries.Received(2).GetChannelsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickResume_Rejected_ShowsTheTextOfRejected()
    {
        // Arrange
        GivenEmailDown();
        DeliveryCommands.ResumeChannelAsync(default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(new Error(Errors.Rejected)));
        var cut = RenderPage("Email");

        // Act
        cut.Find("[data-action=resume]").Click();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find("[data-action-error] .alert-danger").TextContent);
        DeliveryCommands.Received(1).ResumeChannelAsync(Email, Arg.Any<CancellationToken>());
    }

    private static ChannelStatus Status(ChannelId channel, int pending, int pendingTest, DateTime? oldestDue) =>
        new(new ChannelDescriptor<TestChannelSettings>(channel, channel.Name, ChannelFeatures.None), null, pending, pendingTest, oldestDue);

    private void GivenEmailDown() =>
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryChannel>>(
        [
            Channels[0] with { Current = new(Email, 1, ChannelStateKind.Down, Now.AddMinutes(-5), null, null, ChannelDownReason.Operator, null, null, null, null) },
            Channels[1],
            Channels[2],
        ]));

    private IRenderedComponent<ChannelViewPage> RenderPage(string channel) => Render<ChannelViewPage>(p => p.Add(x => x.ChannelName, channel));

    private sealed record TestChannelSettings : ChannelSettings;
}
