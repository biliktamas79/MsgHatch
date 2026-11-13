using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
{
    private static readonly MessageType _type = new("TestEvent");

    [Fact]
    public async Task CountAsync_MessagesOfTwoTenantsChannelsStatusesAndTestFlags_GivesOneRowForEachOfAllTenants()
    {
        // Arrange
        var due = Now.AddMinutes(1);
        await AddAsync(AMessage(T1, nextTryAtUtc: due));
        await AddAsync(AMessage(T2, nextTryAtUtc: Now));
        await AddAsync(Finished(AMessage(T1), DeliveryStatus.Done, Now));
        await AddAsync(AMessage(T1, channel: TestChannel.SecondId, nextTryAtUtc: due));
        await AddAsync(AMessage(T2, flags: MessageFlags.Test, nextTryAtUtc: due));

        // Act
        var counts = await Store.CountAsync(new TrafficQuery(IncludeTest: true), Ct);

        // Assert
        Assert.Equal(
            [
                new MessageCount(_type, TestChannel.SecondId, DeliveryStatus.Pending, Test: false, 1, due),
                new MessageCount(_type, TestChannel.Id, DeliveryStatus.Pending, Test: false, 2, Now),
                new MessageCount(_type, TestChannel.Id, DeliveryStatus.Pending, Test: true, 1, due),
                new MessageCount(_type, TestChannel.Id, DeliveryStatus.Done, Test: false, 1, null),
            ],
            Sorted(counts.Value));
    }

    [Fact]
    public async Task CountAsync_IncludeTestFalse_GivesNoRowOfTheTestMessages()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await AddAsync(AMessage(T1, flags: MessageFlags.Test));

        // Act
        var counts = await Store.CountAsync(new TrafficQuery(), Ct);

        // Assert
        Assert.Equal([new MessageCount(_type, TestChannel.Id, DeliveryStatus.Pending, Test: false, 1, Now)], counts.Value);
    }

    [Fact]
    public async Task CountAsync_Status_CountsOnlyTheMessagesOfThatStatus()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await AddAsync(Finished(AMessage(T1), DeliveryStatus.Failed, Now));
        await AddAsync(Finished(AMessage(T2), DeliveryStatus.Failed, Now));

        // Act
        var counts = await Store.CountAsync(new TrafficQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Equal([new MessageCount(_type, TestChannel.Id, DeliveryStatus.Failed, Test: false, 2, null)], counts.Value);
    }

    [Theory]
    [InlineData(0, null, 2)]
    [InlineData(null, 0, 1)]
    [InlineData(-1, 1, 2)]
    public async Task CountAsync_CreatedRange_IncludesTheStartAndLeavesOutTheEnd(int? fromSeconds, int? toSeconds, int expected)
    {
        // Arrange
        await AddAsync(Created(AMessage(T1), Now.AddSeconds(-1)));
        await AddAsync(Created(AMessage(T1), Now));
        await AddAsync(Created(AMessage(T1), Now.AddSeconds(1)));
        var query = new TrafficQuery(CreatedFromUtc: SecondsLater(fromSeconds), CreatedToUtc: SecondsLater(toSeconds));

        // Act
        var counts = await Store.CountAsync(query, Ct);

        // Assert
        Assert.Equal(expected, Assert.Single(counts.Value).Count);
    }

    protected static OutgoingMessage<TestEnvelope> Finished(OutgoingMessage<TestEnvelope> message, DeliveryStatus status, DateTime? lastTryAtUtc) =>
        message with { Delivery = message.Delivery with { Status = status, LastTryAtUtc = lastTryAtUtc } };

    protected static OutgoingMessage<TestEnvelope> Created(OutgoingMessage<TestEnvelope> message, DateTime createdAtUtc) =>
        message with { Delivery = message.Delivery with { CreatedAtUtc = createdAtUtc } };

    protected DateTime? SecondsLater(int? seconds) => seconds is { } value ? Now.AddSeconds(value) : null;

    protected static IReadOnlyList<MessageCount> Sorted(IReadOnlyList<MessageCount> counts) =>
        [.. counts.OrderBy(static count => count.Channel?.Name, StringComparer.Ordinal).ThenBy(static count => count.Status).ThenBy(static count => count.Test)];
}
