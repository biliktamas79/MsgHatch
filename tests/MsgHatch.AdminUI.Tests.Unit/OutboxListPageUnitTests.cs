using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class OutboxListPageUnitTests : RegistryPageContext
{
    private static readonly MessageType _requests = new("Requests");
    private static readonly Guid _source = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly DateTime _created = new(2030, 1, 1, 10, 0, 5, DateTimeKind.Utc);
    private static readonly MessageCursor _next = new(_created, IdOf(9));
    private static readonly OutboxQuery _failed = new(Status: DeliveryStatus.Failed);

    [Fact]
    public void Render_TestMessage_ShowsTheColumnsOfTheMessageWithTheBadgeTest()
    {
        GivenOutbox(next: null, Outgoing(1, flags: MessageFlags.Test));

        var cut = Render<OutboxListPage>();

        var cells = cut.FindAll("tr[data-row] td");
        Assert.Equal(
            ["2030-01-01 10:00:05 UTC", "Requests", "Email", "User 1", "3 / 8", "Timeout of the provider", "View"],
            cells.Where(static (_, index) => index != 1).Select(SpacedTextOf));
        Assert.Equal(["Failed", "Test"], cells[1].QuerySelectorAll(".badge").Select(static badge => badge.TextContent));
    }

    [Fact]
    public void Render_MessageWithNoTestFlag_ShowsNoBadgeTest()
    {
        GivenOutbox(next: null, Outgoing(1));

        var cut = Render<OutboxListPage>();

        Assert.Equal(("Failed", 0), (cut.Find("[data-status]").TextContent, cut.FindAll("[data-test]").Count));
    }

    [Fact]
    public void Render_Message_LinksViewToThePageOfTheMessage()
    {
        GivenOutbox(next: null, Outgoing(1));

        var cut = Render<OutboxListPage>();

        Assert.Equal("msghatch/outbox/00000000-0000-0000-0000-000000000001", cut.Find("[data-action=view]").GetAttribute("href"));
    }

    [Fact]
    public void Render_EmptyPage_ShowsTheEmptyTextAndThePager()
    {
        var cut = Render<OutboxListPage>();

        Assert.Equal(
            ("There are no outgoing messages.", true),
            (cut.Find("[data-state=empty]").TextContent, cut.Find("[data-list-pager]").QuerySelector("[data-page=older]") is not null));
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        DeliveryQueries.GetOutgoingMessagesAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<MessagePage<OutgoingMessageSummary>>(Errors.Unavailable));

        var cut = Render<OutboxListPage>();

        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        DeliveryQueries.GetOutgoingMessagesAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<MessagePage<OutgoingMessageSummary>>(Errors.Rejected));

        var cut = Render<OutboxListPage>();

        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_NoAddressFilter_ReadsTheFirstPageWithNoFilter()
    {
        Render<OutboxListPage>();

        DeliveryQueries.Received(1).GetOutgoingMessagesAsync(new OutboxQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_AddressOfOutboxOf_StartsWithEachFilterOfTheAddress()
    {
        var target = new DeliveryTarget(DeliveryTargetKind.Company, "a b&c");
        Navigation.NavigateTo(
            DeliveryAdminRoutes.OutboxOf(DeliveryStatus.Failed, _requests, Email, target, _source, _created, _created.AddDays(1), includeTest: true).TrimStart('/'));

        var cut = Render<OutboxListPage>();

        DeliveryQueries.Received(1).GetOutgoingMessagesAsync(
            new OutboxQuery(null, DeliveryStatus.Failed, _requests, Email, target, _source, _created, _created.AddDays(1), IncludeTest: true),
            Arg.Any<CancellationToken>());
        Assert.Equal(
            ("Failed", "Company", "a b&c", true),
            (cut.Find("#msghatch-outbox-status").GetAttribute("value"), cut.Find("#msghatch-outbox-target-kind").GetAttribute("value"),
                cut.Find("#msghatch-outbox-target-id").GetAttribute("value"), cut.Find("#msghatch-outbox-test").HasAttribute("checked")));
    }

    [Fact]
    public void ChangeStatus_Failed_ReadsTheFirstPageWithTheStatusAtOnce()
    {
        var cut = Render<OutboxListPage>();

        cut.Find("#msghatch-outbox-status").Change("Failed");

        DeliveryQueries.Received(1).GetOutgoingMessagesAsync(_failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChangeCreatedTimes_FromAndTo_ReadsTheFirstPageWithTheTimesInUtc()
    {
        var cut = Render<OutboxListPage>();

        cut.Find("#msghatch-outbox-from").Change("2030-01-01T10:00:05");
        cut.Find("#msghatch-outbox-to").Change("2030-01-02T10:00:05");

        var query = DeliveryQueries.ReceivedCalls().Select(static call => (OutboxQuery)call.GetArguments()[0]!).Last();
        Assert.Equal(
            (new OutboxQuery(CreatedFromUtc: _created, CreatedToUtc: _created.AddDays(1)), DateTimeKind.Utc, DateTimeKind.Utc),
            (query, query.CreatedFromUtc!.Value.Kind, query.CreatedToUtc!.Value.Kind));
    }

    [Fact]
    public void ChangeTest_Checked_ReadsTheFirstPageWithTheTestMessages()
    {
        var cut = Render<OutboxListPage>();

        cut.Find("#msghatch-outbox-test").Change(true);

        DeliveryQueries.Received(1).GetOutgoingMessagesAsync(new OutboxQuery(IncludeTest: true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InputTextFilters_AfterThePause_ReadsTheFirstPageWithEachFilter()
    {
        var cut = Render<OutboxListPage>();

        cut.Find("#msghatch-outbox-type").Input("Requests");
        cut.Find("#msghatch-outbox-channel").Input("Email");
        cut.Find("#msghatch-outbox-target-kind").Change("User");
        cut.Find("#msghatch-outbox-target-id").Input("1");
        cut.Find("#msghatch-outbox-source").Input(_source.ToString());
        Time.Advance(TimeSpan.FromMilliseconds(500));

        cut.WaitForAssertion(() => DeliveryQueries.Received(1).GetOutgoingMessagesAsync(
            new OutboxQuery(Type: _requests, Channel: Email, Target: new DeliveryTarget(DeliveryTargetKind.User, "1"), SourceMessageId: _source),
            Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void InputType_BeforeAndAfterThePause_ReadsThePageOnlyAfter500Milliseconds()
    {
        var cut = Render<OutboxListPage>();
        var withType = new OutboxQuery(Type: _requests);

        cut.Find("#msghatch-outbox-type").Input("Requests");
        Time.Advance(TimeSpan.FromMilliseconds(499));
        var beforeThePause = DeliveryQueries.ReceivedCalls().Count(call => withType.Equals(call.GetArguments()[0]));
        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => DeliveryQueries.Received(1).GetOutgoingMessagesAsync(withType, Arg.Any<CancellationToken>()));
        Assert.Equal(0, beforeThePause);
    }

    [Fact]
    public void InputSource_NotAnId_ShowsValidationFailedAndReadsNothing()
    {
        var cut = Render<OutboxListPage>();
        DeliveryQueries.ClearReceivedCalls();

        cut.Find("#msghatch-outbox-source").Input("not an id");
        Time.Advance(TimeSpan.FromMilliseconds(500));

        cut.WaitForAssertion(() => Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code")));
        DeliveryQueries.DidNotReceiveWithAnyArgs().GetOutgoingMessagesAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeStatus_WhileAnEarlierQueryRuns_ShowsOnlyTheAnswerOfTheLastQuery()
    {
        var earlier = new TaskCompletionSource<ErrorOr<MessagePage<OutgoingMessageSummary>>>();
        DeliveryQueries.GetOutgoingMessagesAsync(new OutboxQuery(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>>(earlier.Task));
        DeliveryQueries.GetOutgoingMessagesAsync(_failed, Arg.Any<CancellationToken>()).Returns(Ok(new MessagePage<OutgoingMessageSummary>([Outgoing(2)], null)));
        var cut = Render<OutboxListPage>();

        cut.Find("#msghatch-outbox-status").Change("Failed");
        await cut.InvokeAsync(() => earlier.SetResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(new([Outgoing(1)], null))));

        Assert.Equal([$"msghatch/outbox/{IdOf(2)}"], cut.FindAll("[data-action=view]").Select(static link => link.GetAttribute("href")));
    }

    [Fact]
    public void Render_FirstPageWithNoNextCursor_DisablesNewerAndOlder()
    {
        GivenOutbox(next: null, Outgoing(1));

        var cut = Render<OutboxListPage>();

        Assert.Equal((true, true), (cut.Find("[data-page=newer]").HasAttribute("disabled"), cut.Find("[data-page=older]").HasAttribute("disabled")));
    }

    [Fact]
    public void ClickOlder_PageWithANextCursor_ReadsThePageAfterTheCursorAndEnablesNewer()
    {
        GivenOutbox(_next, Outgoing(1));
        var cut = Render<OutboxListPage>();

        cut.Find("[data-page=older]").Click();

        DeliveryQueries.Received(1).GetOutgoingMessagesAsync(new OutboxQuery(After: _next), Arg.Any<CancellationToken>());
        Assert.False(cut.Find("[data-page=newer]").HasAttribute("disabled"));
    }

    [Fact]
    public void ClickNewer_SecondPage_ReadsTheFirstPageAgainAndDisablesNewer()
    {
        GivenOutbox(_next, Outgoing(1));
        var cut = Render<OutboxListPage>();
        cut.Find("[data-page=older]").Click();

        cut.Find("[data-page=newer]").Click();

        DeliveryQueries.Received(2).GetOutgoingMessagesAsync(new OutboxQuery(), Arg.Any<CancellationToken>());
        Assert.True(cut.Find("[data-page=newer]").HasAttribute("disabled"));
    }

    [Fact]
    public void ChangeStatus_SecondPage_ReadsTheFirstPageWithTheStatus()
    {
        GivenOutbox(_next, Outgoing(1));
        var cut = Render<OutboxListPage>();
        cut.Find("[data-page=older]").Click();

        cut.Find("#msghatch-outbox-status").Change("Failed");

        DeliveryQueries.Received(1).GetOutgoingMessagesAsync(_failed, Arg.Any<CancellationToken>());
        Assert.True(cut.Find("[data-page=newer]").HasAttribute("disabled"));
    }

    [Fact]
    public void Render_NoFilter_DisablesRetryFilteredAndCancelFiltered()
    {
        var cut = Render<OutboxListPage>();

        Assert.Equal((true, true), BulkActionsDisabled(cut));
    }

    [Fact]
    public void ChangeTest_OnlyTheTestFlag_KeepsRetryFilteredAndCancelFilteredDisabled()
    {
        var cut = Render<OutboxListPage>();

        cut.Find("#msghatch-outbox-test").Change(true);

        Assert.Equal((true, true), BulkActionsDisabled(cut));
    }

    [Fact]
    public void ChangeStatus_Failed_EnablesRetryFilteredAndCancelFiltered()
    {
        var cut = Render<OutboxListPage>();

        cut.Find("#msghatch-outbox-status").Change("Failed");

        Assert.Equal((false, false), BulkActionsDisabled(cut));
    }

    [Fact]
    public void ClickRetryFiltered_StatusFailed_AsksTheQuestionAndRetriesNothing()
    {
        var cut = Render<OutboxListPage>();
        cut.Find("#msghatch-outbox-status").Change("Failed");

        cut.Find("[data-action=retry-filtered]").Click();

        Assert.Equal("Retry filtered", cut.Find("[data-dialog] .modal-title").TextContent);
        Assert.Contains("Retry each outgoing message that matches the filters", cut.Find("[data-question]").TextContent, StringComparison.Ordinal);
        DeliveryCommands.DidNotReceiveWithAnyArgs().RetryOutgoingAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmRetryFiltered_StatusFailed_RetriesTheFilterShowsTheCountsAndReadsTheListAgain()
    {
        DeliveryCommands.RetryOutgoingAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new DeliveryActionResult(5, 3)));
        var cut = Render<OutboxListPage>();
        cut.Find("#msghatch-outbox-status").Change("Failed");

        cut.Find("[data-action=retry-filtered]").Click();
        cut.Find("[data-action=confirm]").Click();

        DeliveryCommands.Received(1).RetryOutgoingAsync(_failed, Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetOutgoingMessagesAsync(_failed, Arg.Any<CancellationToken>());
        Assert.Equal(("Matched 5, changed 3.", 0), (cut.Find("[data-action-result]").TextContent, cut.FindAll("[data-dialog]").Count));
    }

    [Fact]
    public void ConfirmCancelFiltered_StatusFailed_CancelsTheFilterAndShowsTheCounts()
    {
        DeliveryCommands.CancelOutgoingAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new DeliveryActionResult(2, 0)));
        var cut = Render<OutboxListPage>();
        cut.Find("#msghatch-outbox-status").Change("Failed");

        cut.Find("[data-action=cancel-filtered]").Click();
        cut.Find("[data-action=confirm]").Click();

        DeliveryCommands.Received(1).CancelOutgoingAsync(_failed, Arg.Any<CancellationToken>());
        DeliveryCommands.DidNotReceiveWithAnyArgs().RetryOutgoingAsync(default!, Arg.Any<CancellationToken>());
        Assert.Equal("Matched 2, changed 0.", cut.Find("[data-action-result]").TextContent);
    }

    [Fact]
    public void ClickBack_QuestionOfCancelFiltered_ClosesTheDialogAndCancelsNothing()
    {
        var cut = Render<OutboxListPage>();
        cut.Find("#msghatch-outbox-status").Change("Failed");
        cut.Find("[data-action=cancel-filtered]").Click();

        cut.Find("[data-action=back]").Click();

        Assert.Empty(cut.FindAll("[data-dialog]"));
        DeliveryCommands.DidNotReceiveWithAnyArgs().CancelOutgoingAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmRetryFiltered_Rejected_ShowsTheTextOfRejected()
    {
        DeliveryCommands.RetryOutgoingAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<DeliveryActionResult>(Errors.Rejected));
        var cut = Render<OutboxListPage>();
        cut.Find("#msghatch-outbox-status").Change("Failed");

        cut.Find("[data-action=retry-filtered]").Click();
        cut.Find("[data-action=confirm]").Click();

        Assert.Equal(AdminErrors.RejectedText, cut.Find("[data-action-result].alert-danger").TextContent);
    }

    [Fact]
    public void ChangeStatus_AfterAResult_HidesTheResult()
    {
        DeliveryCommands.RetryOutgoingAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new DeliveryActionResult(5, 3)));
        var cut = Render<OutboxListPage>();
        cut.Find("#msghatch-outbox-status").Change("Failed");
        cut.Find("[data-action=retry-filtered]").Click();
        cut.Find("[data-action=confirm]").Click();

        cut.Find("#msghatch-outbox-status").Change("Pending");

        Assert.Empty(cut.FindAll("[data-action-result]"));
    }

    private static (bool Retry, bool Cancel) BulkActionsDisabled(IRenderedComponent<OutboxListPage> cut) =>
        (cut.Find("[data-action=retry-filtered]").HasAttribute("disabled"), cut.Find("[data-action=cancel-filtered]").HasAttribute("disabled"));

    private static Guid IdOf(int index) => Guid.Parse($"00000000-0000-0000-0000-{index:D12}");

    private static OutgoingMessageSummary Outgoing(int index, DeliveryStatus status = DeliveryStatus.Failed, MessageFlags flags = MessageFlags.None) =>
        new(
            new DeliveryState(IdOf(index), status, 3, 8, _created, _created, _created, null, null, "Timeout of the provider"),
            _source, null, _requests, Email, new DeliveryTarget(DeliveryTargetKind.User, "1"), "application/json", 1, flags);

    private void GivenOutbox(MessageCursor? next, params OutgoingMessageSummary[] items) =>
        DeliveryQueries.GetOutgoingMessagesAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new MessagePage<OutgoingMessageSummary>(items, next)));
}
