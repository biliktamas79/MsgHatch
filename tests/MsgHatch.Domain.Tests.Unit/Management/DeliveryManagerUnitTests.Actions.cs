using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Management;

public sealed partial class DeliveryManagerUnitTests
{
    [Fact]
    public async Task PauseChannelAsync_RegisteredChannel_SetsAnOperatorDownStateWithTheTimeAndTheNote()
    {
        // Arrange
        _outboxDispatch.SetChannelDownAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<bool>.ForValue(true)));

        // Act
        var error = await Manager().PauseChannelAsync(TestChannel.Id, "Ősz-űr outage", Ct);

        // Assert
        Assert.Null(error);
        await _outboxDispatch.Received(1).SetChannelDownAsync(
            new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "Ősz-űr outage"),
            Ct);
    }

    [Fact]
    public async Task ResumeChannelAsync_RegisteredChannel_EndsEachReasonAndNotifiesTheTopicOfTheChannel()
    {
        // Arrange
        _outboxDispatch.SetChannelUpAsync(default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<bool>.ForValue(true)));

        // Act
        var error = await Manager().ResumeChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Null(error);
        await _outboxDispatch.Received(1).SetChannelUpAsync(TestChannel.Id, null, Ct);
        await _signal.Received(1).NotifyAsync(DispatchTopic.ForChannel(TestChannel.Id), Ct);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task ChannelAction_UnregisteredChannel_GivesNotFoundAndCallsNoStore(string action)
    {
        // Arrange
        var manager = Manager();
        var unregistered = new ChannelId("Unregistered");

        // Act
        var error = action == "pause"
            ? await manager.PauseChannelAsync(unregistered, note: null, Ct)
            : await manager.ResumeChannelAsync(unregistered, Ct);

        // Assert
        Assert.Equal((Errors.NotFound, 0), (error?.Type, _outboxDispatch.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task RetryOutgoingAsync_ThreePages_CallsTheStoreOneTimeForEachPageAndSumsTheCounts()
    {
        // Arrange
        var (first, second) = (new MessageCursor(Now, Guid.NewGuid()), new MessageCursor(Now.AddMinutes(-1), Guid.NewGuid()));
        _outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(
            Page(first, ASummary(), ASummary()), Page(second, ASummary(), ASummary()), Page(null, ASummary()));
        _outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(2), Changed(1), Changed(0));

        // Act
        var result = await Manager().RetryOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Equal(new DeliveryActionResult(5, 3), result.Value);
        await _outbox.Received(3).RetryAsync(Arg.Any<IReadOnlyList<Guid>>(), Now, 3, Ct);
        await _outbox.Received(1).QueryAsync(Arg.Is<OutboxQuery>(query => query.After == second && query.Limit == 500), Ct);
    }

    [Fact]
    public async Task RetryOutgoingAsync_TwoContentTypesOnAPage_GivesEachTheTriesOfItsRetryOptions()
    {
        // Arrange
        var (withThree, withEight) = (ASummary("Test:TestEnvelope"), ASummary("Test:OtherEnvelope"));
        _outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, withThree, withEight));
        _outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(1));

        // Act
        await Manager().RetryOutgoingAsync(new OutboxQuery(Channel: TestChannel.Id), Ct);

        // Assert
        await _outbox.Received(1).RetryAsync(Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { withThree.Delivery.Id })), Now, 3, Ct);
        await _outbox.Received(1).RetryAsync(Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { withEight.Delivery.Id })), Now, 8, Ct);
    }

    [Fact]
    public async Task RetryOutgoingAsync_Changes_NotifiesTheTopicOfEachChangedChannel()
    {
        // Arrange
        var onSecond = ASummary("Test:OtherEnvelope") with { Channel = TestChannel.SecondId };
        _outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, ASummary(), onSecond));
        _outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(1), Changed(0));

        // Act
        await Manager().RetryOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Equal(
            [DispatchTopic.ForChannel(TestChannel.Id)],
            _signal.ReceivedCalls().Select(static call => (DispatchTopic)call.GetArguments()[0]!));
    }

    [Fact]
    public async Task RetryOutgoingAsync_NoChange_NotifiesNothing()
    {
        // Arrange
        _outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, ASummary()));
        _outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(0));

        // Act
        await Manager().RetryOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Empty(_signal.ReceivedCalls());
    }

    [Fact]
    public async Task RetryReceivedAsync_Changes_RetriesWithTheInboxOptionsAndNotifiesTheInboxTopic()
    {
        // Arrange
        var summary = AReceivedSummary();
        _inbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<MessagePage<ReceivedMessageSummary>>.ForValue(new MessagePage<ReceivedMessageSummary>([summary], Next: null))));
        _inbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(1));

        // Act
        var result = await Manager().RetryReceivedAsync(new InboxQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Equal(new DeliveryActionResult(1, 1), result.Value);
        await _inbox.Received(1).RetryAsync(Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { summary.Delivery.Id })), Now, 8, Ct);
        await _signal.Received(1).NotifyAsync(DispatchTopic.Inbox, Ct);
    }

    [Fact]
    public async Task CancelReceivedAsync_Page_CancelsTheIdsOfThePage()
    {
        // Arrange
        var summary = AReceivedSummary();
        _inbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<MessagePage<ReceivedMessageSummary>>.ForValue(new MessagePage<ReceivedMessageSummary>([summary], Next: null))));
        _inbox.CancelAsync(default!, default, Ct).ReturnsForAnyArgs(Changed(1));

        // Act
        var result = await Manager().CancelReceivedAsync(new InboxQuery(Status: DeliveryStatus.Pending), Ct);

        // Assert
        Assert.Equal(new DeliveryActionResult(1, 1), result.Value);
        await _inbox.Received(1).CancelAsync(Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { summary.Delivery.Id })), Now, Ct);
    }

    [Fact]
    public async Task CancelOutgoingAsync_TwoPages_SumsTheCountsAndNotifiesNothing()
    {
        // Arrange
        _outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(new MessageCursor(Now, Guid.NewGuid()), ASummary()), Page(null, ASummary()));
        _outbox.CancelAsync(default!, default, Ct).ReturnsForAnyArgs(Changed(1), Changed(0));

        // Act
        var result = await Manager().CancelOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Pending), Ct);

        // Assert
        Assert.Equal((new DeliveryActionResult(2, 1), 0), (result.Value, _signal.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task RetryReceivedAsync_NoInboxStore_ChangesNothing()
    {
        // Act
        var result = await Manager(withInbox: false).RetryReceivedAsync(new InboxQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Equal(new DeliveryActionResult(0, 0), result.Value);
    }

    [Fact]
    public async Task PurgeAsync_MaxCount100_CallsEachDispatchStoreWithMaxCount100AndGivesBothCounts()
    {
        // Arrange
        var before = Now.AddDays(-30);
        _inboxDispatch.PurgeAsync(before, 100, Ct).Returns(Changed(7));
        _outboxDispatch.PurgeAsync(before, 100, Ct).Returns(Changed(9));

        // Act
        var deleted = await Manager().PurgeAsync(before, 100, Ct);

        // Assert
        Assert.Equal(new DeletedMessages(7, 9), deleted.Value);
    }

    [Fact]
    public async Task DeleteTestDataAsync_TwoDispatchStores_CallsBothAndGivesBothCounts()
    {
        // Arrange
        _inboxDispatch.DeleteTestDataAsync(Ct).Returns(Changed(2));
        _outboxDispatch.DeleteTestDataAsync(Ct).Returns(Changed(5));

        // Act
        var deleted = await Manager().DeleteTestDataAsync(Ct);

        // Assert
        Assert.Equal(new DeletedMessages(2, 5), deleted.Value);
    }

    private static ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> Page(MessageCursor? next, params OutgoingMessageSummary[] items) =>
        ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(new MessagePage<OutgoingMessageSummary>(items, next)));

    private static ValueTask<ErrorOr<int>> Changed(int count) => ValueTask.FromResult(ErrorOr<int>.ForValue(count));
}
