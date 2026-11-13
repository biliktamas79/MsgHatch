using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static MsgHatch.Domain.Tests.Unit.Contracts.ManagementTestRows;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class InboxStoreTestsBase<TStore>
{
    [Theory]
    [MemberData(nameof(ManagementTestRows.InboxQueryRows), MemberType = typeof(ManagementTestRows))]
    public async Task QueryAsync_Filter_GivesTheMatchingMessagesOfTheTenantNewestFirst(string row)
    {
        // Arrange
        await SeedQueryScenarioAsync();
        var (query, expected) = InboxRow(row);

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
        await AddAsync(CreatedMinutesAgo(ids[4], 5));
        await AddAsync(CreatedMinutesAgo(ids[0], 2));
        await AddAsync(CreatedMinutesAgo(ids[3], 4));
        await AddAsync(CreatedMinutesAgo(ids[1], 2));
        await AddAsync(CreatedMinutesAgo(ids[2], 3));
        var first = await TenantStore(T1).QueryAsync(new InboxQuery(Limit: 2), Ct);
        await TenantStore(T1).CancelAsync([ids[2]], Now, Ct);

        // Act
        var second = await TenantStore(T1).QueryAsync(new InboxQuery(Limit: 2, After: first.Value.Next), Ct);
        var third = await TenantStore(T1).QueryAsync(new InboxQuery(Limit: 2, After: second.Value.Next), Ct);

        // Assert
        Assert.Equal(ids, first.Value.Items.Concat(second.Value.Items).Concat(third.Value.Items).Select(static summary => summary.Delivery.Id));
        Assert.Null(third.Value.Next);
    }

    [Fact]
    public async Task QueryAsync_Message_GivesItsSummaryWithAllHeaderFields()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1, flags: MessageFlags.Test));

        // Act
        var page = await TenantStore(T1).QueryAsync(new InboxQuery(IncludeTest: true), Ct);

        // Assert
        Assert.Equal(ReceivedMessageSummary.Of(message), Assert.Single(page.Value.Items));
    }

    protected static ReceivedMessage<TestEvent> With(ReceivedMessage<TestEvent> message, Func<DeliveryState, DeliveryState> change) =>
        message with { Delivery = change(message.Delivery) };

    private ReceivedMessage<TestEvent> CreatedMinutesAgo(Guid id, int minutes) =>
        With(AMessage(T1, id: id), delivery => delivery with { CreatedAtUtc = Now.AddMinutes(-minutes) });

    private async Task SeedQueryScenarioAsync()
    {
        await AddAsync(With(AMessage(T1, id: A, nextTryAtUtc: Start.AddHours(-1)), static delivery => delivery with { CreatedAtUtc = Start.AddHours(-2) }));
        await AddAsync(With(AMessage(T1, id: B, nextTryAtUtc: Start.AddHours(1)), static delivery => delivery with
        {
            Status = DeliveryStatus.Failed, CreatedAtUtc = Start.AddHours(-1),
        }));
        await AddAsync(With(AMessage(T1, id: X, flags: MessageFlags.Test, nextTryAtUtc: Start.AddHours(1)), static delivery => delivery with
        {
            CreatedAtUtc = Start.AddMinutes(-30),
        }));
        await AddAsync(With(AMessage(T2, id: D, nextTryAtUtc: Start.AddHours(1)), static delivery => delivery with
        {
            Status = DeliveryStatus.Failed, CreatedAtUtc = Start.AddHours(-1),
        }));
    }
}
