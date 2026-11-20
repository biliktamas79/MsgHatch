using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class TrafficPageUnitTests : RegistryPageContext
{
    private static readonly MessageType _requests = new("Requests");
    private static readonly DateTime _from = new(2030, 1, 1, 10, 0, 5, DateTimeKind.Utc);

    public TrafficPageUnitTests()
    {
        DeliveryQueries.GetTrafficAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new TrafficReport(
            [new MessageCount(_requests, null, DeliveryStatus.Failed, Test: true, 4, null)],
            [
                new MessageCount(_requests, Email, DeliveryStatus.Pending, Test: true, 7, new DateTime(2029, 12, 31, 23, 55, 0, DateTimeKind.Utc)),
                new MessageCount(_requests, Sms, DeliveryStatus.Done, Test: false, 12, null),
            ])));
    }

    [Fact]
    public void Render_NoFilter_ReadsTheTrafficWithNoFilter()
    {
        Render<TrafficPage>();

        DeliveryQueries.Received(1).GetTrafficAsync(new TrafficQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Report_ShowsTheRowsOfTheInboxAndOfTheOutbox()
    {
        var cut = Render<TrafficPage>();

        Assert.Equal(["Requests Failed Yes 4"], cut.FindAll("tr[data-inbox-row]").Select(SpacedTextOf));
        Assert.Equal(
            ["Requests Email Pending Yes 7 2029-12-31 23:55:00 UTC", "Requests Sms Done No 12"],
            cut.FindAll("tr[data-outbox-row]").Select(SpacedTextOf));
    }

    [Fact]
    public void Render_TimeFilters_LinksEachCountToItsListWithTheFiltersOfItsRow()
    {
        var cut = Render<TrafficPage>();

        cut.Find("#msghatch-traffic-from").Change("2030-01-01T10:00:05");

        Assert.Equal(
            [
                DeliveryAdminRoutes.InboxOf(DeliveryStatus.Failed, _requests, _from, includeTest: true).TrimStart('/'),
                DeliveryAdminRoutes.OutboxOf(DeliveryStatus.Pending, _requests, Email, createdFromUtc: _from, includeTest: true).TrimStart('/'),
                DeliveryAdminRoutes.OutboxOf(DeliveryStatus.Done, _requests, Sms, createdFromUtc: _from).TrimStart('/'),
            ],
            cut.FindAll("a[data-count]").Select(static link => link.GetAttribute("href")));
    }

    [Fact]
    public void ChangeStatus_Failed_ReadsTheTrafficWithTheStatusAtOnce()
    {
        var cut = Render<TrafficPage>();

        cut.Find("#msghatch-traffic-status").Change("Failed");

        DeliveryQueries.Received(1).GetTrafficAsync(new TrafficQuery(Status: DeliveryStatus.Failed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChangeFilters_StatusTimesAndTest_ReadsTheTrafficWithEachFilter()
    {
        var cut = Render<TrafficPage>();

        cut.Find("#msghatch-traffic-status").Change("Failed");
        cut.Find("#msghatch-traffic-from").Change("2030-01-01T10:00:05");
        cut.Find("#msghatch-traffic-to").Change("2030-01-02T10:00:05");
        cut.Find("#msghatch-traffic-test").Change(true);

        var query = DeliveryQueries.ReceivedCalls().Select(static call => (TrafficQuery)call.GetArguments()[0]!).Last();
        Assert.Equal(
            (new TrafficQuery(_from, _from.AddDays(1), DeliveryStatus.Failed, IncludeTest: true), DateTimeKind.Utc),
            (query, query.CreatedFromUtc!.Value.Kind));
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailableAndNoList()
    {
        DeliveryQueries.GetTrafficAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<TrafficReport>(Errors.Unavailable));

        var cut = Render<TrafficPage>();

        Assert.Equal((AdminErrors.UnavailableText, 0), (cut.Find(".alert-danger").TextContent, cut.FindAll("[data-section]").Count));
    }

    [Fact]
    public void Render_EmptyReport_ShowsTheEmptyTextOfEachStore()
    {
        DeliveryQueries.GetTrafficAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new TrafficReport([], [])));

        var cut = Render<TrafficPage>();

        Assert.Equal(["There are no received messages.", "There are no outgoing messages."], cut.FindAll("[data-state=empty]").Select(static text => text.TextContent));
    }

    [Fact]
    public void ClickDeleteTestData_AsksTheQuestionAndDeletesNothing()
    {
        var cut = Render<TrafficPage>();

        cut.Find("[data-action=delete-test-data]").Click();

        Assert.Contains("Delete each test message of all tenants", cut.Find("[data-dialog]").TextContent, StringComparison.Ordinal);
        DeliveryCommands.DidNotReceiveWithAnyArgs().DeleteTestDataAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmDeleteTestData_DeletesShowsTheCountsAndReadsTheTrafficAgain()
    {
        DeliveryCommands.DeleteTestDataAsync(Arg.Any<CancellationToken>()).Returns(Ok(new DeletedMessages(3, 5)));
        var cut = Render<TrafficPage>();
        cut.Find("[data-action=delete-test-data]").Click();

        cut.Find("[data-action=confirm-delete]").Click();

        DeliveryCommands.Received(1).DeleteTestDataAsync(Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetTrafficAsync(new TrafficQuery(), Arg.Any<CancellationToken>());
        Assert.Equal(
            ("Deleted 3 received messages and 5 outgoing messages.", 0),
            (cut.Find("[data-deleted-result].alert-success").TextContent, cut.FindAll("[data-dialog]").Count));
    }

    [Fact]
    public void CancelDeleteTestData_ClosesTheQuestionAndDeletesNothing()
    {
        var cut = Render<TrafficPage>();
        cut.Find("[data-action=delete-test-data]").Click();

        cut.Find("[data-action=cancel-delete]").Click();

        Assert.Empty(cut.FindAll("[data-dialog]"));
        DeliveryCommands.DidNotReceiveWithAnyArgs().DeleteTestDataAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmDeleteTestData_Rejected_ShowsTheTextOfRejected()
    {
        DeliveryCommands.DeleteTestDataAsync(Arg.Any<CancellationToken>()).Returns(Fail<DeletedMessages>(Errors.Rejected));
        var cut = Render<TrafficPage>();
        cut.Find("[data-action=delete-test-data]").Click();

        cut.Find("[data-action=confirm-delete]").Click();

        Assert.Equal(AdminErrors.RejectedText, cut.Find("[data-deleted-result].alert-danger").TextContent);
    }

    [Fact]
    public void ClickPurge_OpensTheDialogWithTheCurrentTimeAndAMaximumOf1000()
    {
        Time.Advance(TimeSpan.FromMilliseconds(1500));
        var cut = Render<TrafficPage>();

        cut.Find("[data-action=purge]").Click();

        Assert.Equal(
            ("Purge", "2030-01-01T00:00:01", "1000"),
            (cut.Find("[data-dialog] .modal-title").TextContent, cut.Find("#msghatch-purge-before").GetAttribute("value"),
                cut.Find("#msghatch-purge-max").GetAttribute("value")));
    }

    [Fact]
    public void ConfirmPurge_TimeAndMaximum_PurgesShowsTheCountsAndReadsTheTrafficAgain()
    {
        DeliveryCommands.PurgeAsync(default, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new DeletedMessages(10, 20)));
        var cut = Render<TrafficPage>();
        cut.Find("[data-action=purge]").Click();

        cut.Find("#msghatch-purge-before").Change("2029-12-01T00:00:00");
        cut.Find("#msghatch-purge-max").Change("50");
        cut.Find("[data-action=confirm-purge]").Click();

        DeliveryCommands.Received(1).PurgeAsync(
            Arg.Is<DateTime>(time => time == new DateTime(2029, 12, 1, 0, 0, 0, DateTimeKind.Utc) && time.Kind == DateTimeKind.Utc), 50, Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetTrafficAsync(new TrafficQuery(), Arg.Any<CancellationToken>());
        Assert.Equal(
            ("Deleted 10 received messages and 20 outgoing messages.", 0),
            (cut.Find("[data-deleted-result]").TextContent, cut.FindAll("[data-dialog]").Count));
    }

    [Fact]
    public void ConfirmPurge_DefaultValues_PurgesBeforeTheCurrentTimeToTheSecondWithAMaximumOf1000()
    {
        Time.Advance(TimeSpan.FromMilliseconds(1500));
        var cut = Render<TrafficPage>();
        cut.Find("[data-action=purge]").Click();

        cut.Find("[data-action=confirm-purge]").Click();

        DeliveryCommands.Received(1).PurgeAsync(new DateTime(2030, 1, 1, 0, 0, 1, DateTimeKind.Utc), 1000, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmPurge_MaximumZero_ShowsValidationFailedInTheDialogAndPurgesNothing()
    {
        var cut = Render<TrafficPage>();
        cut.Find("[data-action=purge]").Click();

        cut.Find("#msghatch-purge-max").Change("0");
        cut.Find("[data-action=confirm-purge]").Click();

        Assert.Equal("400", cut.Find("[data-dialog] .alert-danger").GetAttribute("data-error-code"));
        DeliveryCommands.DidNotReceiveWithAnyArgs().PurgeAsync(default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmPurge_NoTime_ShowsValidationFailedInTheDialogAndPurgesNothing()
    {
        var cut = Render<TrafficPage>();
        cut.Find("[data-action=purge]").Click();

        cut.Find("#msghatch-purge-before").Change(string.Empty);
        cut.Find("[data-action=confirm-purge]").Click();

        Assert.Equal("400", cut.Find("[data-dialog] .alert-danger").GetAttribute("data-error-code"));
        DeliveryCommands.DidNotReceiveWithAnyArgs().PurgeAsync(default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmPurge_Rejected_ShowsTheTextOfRejectedInTheDialog()
    {
        DeliveryCommands.PurgeAsync(default, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<DeletedMessages>(Errors.Rejected));
        var cut = Render<TrafficPage>();
        cut.Find("[data-action=purge]").Click();

        cut.Find("[data-action=confirm-purge]").Click();

        Assert.Equal(AdminErrors.RejectedText, cut.Find("[data-dialog] .alert-danger").TextContent);
    }

    [Fact]
    public void ClickBack_PurgeDialog_ClosesTheDialogAndPurgesNothing()
    {
        var cut = Render<TrafficPage>();
        cut.Find("[data-action=purge]").Click();

        cut.Find("[data-dialog] [data-action=back]").Click();

        Assert.Empty(cut.FindAll("[data-dialog]"));
        DeliveryCommands.DidNotReceiveWithAnyArgs().PurgeAsync(default, default, Arg.Any<CancellationToken>());
    }
}
