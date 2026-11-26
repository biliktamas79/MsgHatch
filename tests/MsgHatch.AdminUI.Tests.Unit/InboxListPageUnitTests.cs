using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class InboxListPageUnitTests : RegistryPageContext
{
    private static readonly MessageType _requests = new("Requests");
    private static readonly DateTime _created = new(2030, 1, 1, 10, 0, 5, DateTimeKind.Utc);
    private static readonly MessageCursor _next = new(_created, IdOf(9));
    private static readonly InboxQuery _failed = new(Status: DeliveryStatus.Failed);

    [Fact]
    public void Render_TestMessage_ShowsTheColumnsOfTheMessageWithTheBadgeTest()
    {
        // Arrange
        GivenInbox(next: null, Received(1, MessageFlags.Test));

        // Act
        var cut = Render<InboxListPage>();

        // Assert
        var cells = cut.FindAll("tr[data-row] td");
        Assert.Equal(
            ["2030-01-01 10:00:05 UTC", "Requests", "3 / 8", "Timeout of the handler", "View"],
            cells.Where(static (_, index) => index != 1).Select(SpacedTextOf));
        Assert.Equal(["Failed", "Test"], cells[1].QuerySelectorAll(".badge").Select(static badge => badge.TextContent));
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(new InboxQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Message_LinksViewToThePageOfTheMessage()
    {
        // Arrange
        GivenInbox(next: null, Received(1));

        // Act
        var cut = Render<InboxListPage>();

        // Assert
        Assert.Equal("msghatch/inbox/00000000-0000-0000-0000-000000000001", cut.Find("[data-action=view]").GetAttribute("href"));
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(new InboxQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_EmptyPage_ShowsTheEmptyTextAndThePager()
    {
        // Act
        var cut = Render<InboxListPage>();

        // Assert
        Assert.Equal(
            ("There are no received messages.", true),
            (cut.Find("[data-state=empty]").TextContent, cut.Find("[data-list-pager]").QuerySelector("[data-page=older]") is not null));
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(new InboxQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        // Arrange
        DeliveryQueries.GetReceivedMessagesAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<MessagePage<ReceivedMessageSummary>>(Errors.Unavailable));

        // Act
        var cut = Render<InboxListPage>();

        // Assert
        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(new InboxQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        // Arrange
        DeliveryQueries.GetReceivedMessagesAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<MessagePage<ReceivedMessageSummary>>(Errors.Rejected));

        // Act
        var cut = Render<InboxListPage>();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(new InboxQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_NoAddressFilter_ReadsTheFirstPageWithNoFilter()
    {
        // Act
        Render<InboxListPage>();

        // Assert
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(new InboxQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_AddressOfInboxOf_StartsWithEachFilterOfTheAddress()
    {
        // Arrange
        Navigation.NavigateTo(DeliveryAdminRoutes.InboxOf(DeliveryStatus.Failed, _requests, _created, _created.AddDays(1), includeTest: true).TrimStart('/'));

        // Act
        var cut = Render<InboxListPage>();

        // Assert
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(
            new InboxQuery(null, DeliveryStatus.Failed, _requests, _created, _created.AddDays(1), IncludeTest: true), Arg.Any<CancellationToken>());
        Assert.Equal(
            ("Failed", "Requests", true),
            (cut.Find("#msghatch-inbox-status").GetAttribute("value"), cut.Find("#msghatch-inbox-type").GetAttribute("value"),
                cut.Find("#msghatch-inbox-test").HasAttribute("checked")));
    }

    [Fact]
    public void ChangeStatus_Failed_ReadsTheFirstPageWithTheStatusAtOnce()
    {
        // Arrange
        var cut = Render<InboxListPage>();

        // Act
        cut.Find("#msghatch-inbox-status").Change("Failed");

        // Assert
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(_failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChangeCreatedTimesAndTest_TimesAndTestFlag_ReadsTheFirstPageWithTheTimesInUtcAndTheTestMessages()
    {
        // Arrange
        var cut = Render<InboxListPage>();

        // Act
        cut.Find("#msghatch-inbox-from").Change("2030-01-01T10:00:05");
        cut.Find("#msghatch-inbox-to").Change("2030-01-02T10:00:05");
        cut.Find("#msghatch-inbox-test").Change(true);

        // Assert
        var query = DeliveryQueries.ReceivedCalls().Select(static call => (InboxQuery)call.GetArguments()[0]!).Last();
        Assert.Equal(
            (new InboxQuery(CreatedFromUtc: _created, CreatedToUtc: _created.AddDays(1), IncludeTest: true), DateTimeKind.Utc, DateTimeKind.Utc),
            (query, query.CreatedFromUtc!.Value.Kind, query.CreatedToUtc!.Value.Kind));
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(new InboxQuery(CreatedFromUtc: _created), Arg.Any<CancellationToken>());
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(new InboxQuery(CreatedFromUtc: _created, CreatedToUtc: _created.AddDays(1)), Arg.Any<CancellationToken>());
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(
            new InboxQuery(CreatedFromUtc: _created, CreatedToUtc: _created.AddDays(1), IncludeTest: true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InputType_BeforeAndAfterThePause_ReadsThePageOnlyAfter500Milliseconds()
    {
        // Arrange
        var cut = Render<InboxListPage>();
        var withType = new InboxQuery(Type: _requests);

        // Act
        cut.Find("#msghatch-inbox-type").Input("Requests");
        Time.Advance(TimeSpan.FromMilliseconds(499));
        var beforeThePause = DeliveryQueries.ReceivedCalls().Count(call => withType.Equals(call.GetArguments()[0]));
        Time.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        cut.WaitForAssertion(() => DeliveryQueries.Received(1).GetReceivedMessagesAsync(withType, Arg.Any<CancellationToken>()));
        Assert.Equal(0, beforeThePause);
    }

    [Fact]
    public void InputType_NotAName_ShowsValidationFailedAndReadsNothing()
    {
        // Arrange
        var cut = Render<InboxListPage>();
        DeliveryQueries.ClearReceivedCalls();

        // Act
        cut.Find("#msghatch-inbox-type").Input("not a name");
        Time.Advance(TimeSpan.FromMilliseconds(500));

        // Assert
        cut.WaitForAssertion(() => Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code")));
        DeliveryQueries.DidNotReceiveWithAnyArgs().GetReceivedMessagesAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickOlderThenNewer_PageWithANextCursor_ReadsThePageAfterTheCursorAndThenTheFirstPage()
    {
        // Arrange
        GivenInbox(_next, Received(1));
        var cut = Render<InboxListPage>();

        // Act
        cut.Find("[data-page=older]").Click();
        cut.Find("[data-page=newer]").Click();

        // Assert
        DeliveryQueries.Received(1).GetReceivedMessagesAsync(new InboxQuery(After: _next), Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetReceivedMessagesAsync(new InboxQuery(), Arg.Any<CancellationToken>());
        Assert.True(cut.Find("[data-page=newer]").HasAttribute("disabled"));
    }

    [Fact]
    public void Render_NoFilter_DisablesRetryFilteredAndCancelFiltered()
    {
        // Act
        var cut = Render<InboxListPage>();

        // Assert
        Assert.Equal((true, true), BulkActionsDisabled(cut));
    }

    [Fact]
    public void ChangeStatus_Failed_EnablesRetryFilteredAndCancelFiltered()
    {
        // Arrange
        var cut = Render<InboxListPage>();

        // Act
        cut.Find("#msghatch-inbox-status").Change("Failed");

        // Assert
        Assert.Equal((false, false), BulkActionsDisabled(cut));
    }

    [Fact]
    public void ClickRetryFiltered_StatusFailed_AsksTheQuestionOfTheReceivedMessages()
    {
        // Arrange
        var cut = Render<InboxListPage>();
        cut.Find("#msghatch-inbox-status").Change("Failed");

        // Act
        cut.Find("[data-action=retry-filtered]").Click();

        // Assert
        Assert.Contains("Retry each received message that matches the filters", cut.Find("[data-question]").TextContent, StringComparison.Ordinal);
        DeliveryCommands.DidNotReceiveWithAnyArgs().RetryReceivedAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmRetryFiltered_StatusFailed_RetriesTheFilterShowsTheCountsAndReadsTheListAgain()
    {
        // Arrange
        DeliveryCommands.RetryReceivedAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new DeliveryActionResult(5, 3)));
        var cut = Render<InboxListPage>();
        cut.Find("#msghatch-inbox-status").Change("Failed");

        // Act
        cut.Find("[data-action=retry-filtered]").Click();
        cut.Find("[data-action=confirm]").Click();

        // Assert
        Assert.Equal("Matched 5, changed 3.", cut.Find("[data-action-result]").TextContent);
        DeliveryCommands.Received(1).RetryReceivedAsync(_failed, Arg.Any<CancellationToken>());
        DeliveryCommands.DidNotReceiveWithAnyArgs().CancelReceivedAsync(default!, Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetReceivedMessagesAsync(_failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConfirmCancelFiltered_StatusFailed_CancelsTheFilterAndShowsTheCounts()
    {
        // Arrange
        DeliveryCommands.CancelReceivedAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new DeliveryActionResult(2, 1)));
        var cut = Render<InboxListPage>();
        cut.Find("#msghatch-inbox-status").Change("Failed");

        // Act
        cut.Find("[data-action=cancel-filtered]").Click();
        cut.Find("[data-action=confirm]").Click();

        // Assert
        Assert.Equal("Matched 2, changed 1.", cut.Find("[data-action-result]").TextContent);
        DeliveryCommands.Received(1).CancelReceivedAsync(_failed, Arg.Any<CancellationToken>());
        DeliveryCommands.DidNotReceiveWithAnyArgs().RetryReceivedAsync(default!, Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetReceivedMessagesAsync(_failed, Arg.Any<CancellationToken>());
    }

    private static (bool Retry, bool Cancel) BulkActionsDisabled(IRenderedComponent<InboxListPage> cut) =>
        (cut.Find("[data-action=retry-filtered]").HasAttribute("disabled"), cut.Find("[data-action=cancel-filtered]").HasAttribute("disabled"));

    private static Guid IdOf(int index) => Guid.Parse($"00000000-0000-0000-0000-{index:D12}");

    private static ReceivedMessageSummary Received(int index, MessageFlags flags = MessageFlags.None) =>
        new(new DeliveryState(IdOf(index), DeliveryStatus.Failed, 3, 8, _created, _created, _created, null, null, "Timeout of the handler"), null, _requests, 1, flags);

    private void GivenInbox(MessageCursor? next, params ReceivedMessageSummary[] items) =>
        DeliveryQueries.GetReceivedMessagesAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new MessagePage<ReceivedMessageSummary>(items, next)));
}
