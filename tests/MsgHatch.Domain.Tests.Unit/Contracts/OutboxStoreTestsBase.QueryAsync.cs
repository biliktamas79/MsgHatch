using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static MsgHatch.Domain.Tests.Unit.Contracts.ManagementTestRows;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
{
    [Theory]
    [MemberData(nameof(ManagementTestRows.OutboxQueryRows), MemberType = typeof(ManagementTestRows))]
    public async Task QueryAsync_Filter_GivesTheMatchingMessagesOfTheTenantNewestFirst(string row)
    {
        // Arrange
        await SeedQueryScenarioAsync();
        var (query, expected) = OutboxRow(row);

        // Act
        var page = await TenantStore(T1).QueryAsync(query, Ct);

        // Assert
        Assert.Equal(expected, page.Value.Items.Select(static summary => summary.Delivery.Id));
    }

    [Fact]
    public async Task QueryAsync_ThreePagesWhileAMessageChangesItsStatus_GivesEachMessageOneTimeInOrder()
    {
        // Arrange
        Guid[] ids = [HighId, LowId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        int[] minutesAgo = [2, 2, 3, 4, 5];
        await TenantStore(T1).AddAsync(
            [.. ids.Zip(minutesAgo, (id, minutes) => With(AMessage(T1, id: id), delivery => delivery with { CreatedAtUtc = Now.AddMinutes(-minutes) }))],
            Ct);
        var first = await TenantStore(T1).QueryAsync(new OutboxQuery(Limit: 2), Ct);
        await TenantStore(T1).CancelAsync([ids[2]], Now, Ct);

        // Act
        var second = await TenantStore(T1).QueryAsync(new OutboxQuery(Limit: 2, After: first.Value.Next), Ct);
        var third = await TenantStore(T1).QueryAsync(new OutboxQuery(Limit: 2, After: second.Value.Next), Ct);

        // Assert
        Assert.Equal(ids, first.Value.Items.Concat(second.Value.Items).Concat(third.Value.Items).Select(static summary => summary.Delivery.Id));
        Assert.Null(third.Value.Next);
    }

    [Fact]
    public async Task QueryAsync_FullPage_GivesTheCursorOfItsLastMessage()
    {
        // Arrange
        var older = await AddAsync(With(AMessage(T1), delivery => delivery with { CreatedAtUtc = Now.AddMinutes(-1) }));
        await AddAsync(AMessage(T1));

        // Act
        var page = await TenantStore(T1).QueryAsync(new OutboxQuery(Limit: 2), Ct);

        // Assert
        Assert.Equal(new MessageCursor(older.Delivery.CreatedAtUtc, older.Delivery.Id), page.Value.Next);
    }

    [Fact]
    public async Task QueryAsync_Message_GivesItsSummaryWithAllHeaderFields()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1, flags: MessageFlags.Test));

        // Act
        var page = await TenantStore(T1).QueryAsync(new OutboxQuery(IncludeTest: true), Ct);

        // Assert
        Assert.Equal(OutgoingMessageSummary.Of(message), Assert.Single(page.Value.Items));
    }

    protected static OutgoingMessage<TestEnvelope> With(OutgoingMessage<TestEnvelope> message, Func<DeliveryState, DeliveryState> change) =>
        message with { Delivery = change(message.Delivery) };

    private async Task SeedQueryScenarioAsync()
    {
        await AddAsync(With(AMessage(T1, id: A, nextTryAtUtc: Start.AddHours(-1)), static delivery => delivery with
        {
            CreatedAtUtc = Start.AddHours(-2),
        }) with { SourceMessageId = SourceOfA });
        await AddAsync(With(AMessage(T1, id: B, channel: TestChannel.SecondId, nextTryAtUtc: Start.AddHours(1)), static delivery => delivery with
        {
            Status = DeliveryStatus.Failed, CreatedAtUtc = Start.AddHours(-1),
        }) with { SourceMessageId = SourceOfB, Type = OtherType, Target = new DeliveryTarget(DeliveryTargetKind.User, "u2") });
        await AddAsync(With(AMessage(T1, id: X, flags: MessageFlags.Test, nextTryAtUtc: Start.AddHours(1)), static delivery => delivery with
        {
            CreatedAtUtc = Start.AddMinutes(-30),
        }) with { SourceMessageId = SourceOfA });
        await AddAsync(With(AMessage(T2, id: D, channel: TestChannel.SecondId, nextTryAtUtc: Start.AddHours(1)), static delivery => delivery with
        {
            Status = DeliveryStatus.Failed, CreatedAtUtc = Start.AddHours(-1),
        }) with { SourceMessageId = SourceOfB, Type = OtherType, Target = new DeliveryTarget(DeliveryTargetKind.User, "u2") });
    }
}
