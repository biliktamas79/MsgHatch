using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class InboxStoreTestsBase<TStore>
{
    private static readonly MessageType _type = new("TestEvent");

    [Fact]
    public async Task CountAsync_IncludeTest_GivesOneRowForEachStatusAndTestFlagOfTheTenant()
    {
        // Arrange
        var due = Now.AddMinutes(1);
        await AddAsync(AMessage(T1, nextTryAtUtc: due));
        await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddMinutes(2)));
        await AddAsync(Finished(AMessage(T1), DeliveryStatus.Done, Now));
        await AddAsync(AMessage(T1, flags: MessageFlags.Test, nextTryAtUtc: due));
        await AddAsync(AMessage(T2));

        // Act
        var counts = await TenantStore(T1).CountAsync(new TrafficQuery(IncludeTest: true), Ct);

        // Assert
        Assert.Equal(
            [
                new MessageCount(_type, Channel: null, DeliveryStatus.Pending, Test: false, 2, due),
                new MessageCount(_type, Channel: null, DeliveryStatus.Pending, Test: true, 1, due),
                new MessageCount(_type, Channel: null, DeliveryStatus.Done, Test: false, 1, null),
            ],
            counts.Value.OrderBy(static count => count.Status).ThenBy(static count => count.Test));
    }

    [Theory]
    [InlineData(null, false, 2)]
    [InlineData(DeliveryStatus.Failed, false, 1)]
    [InlineData(null, true, 3)]
    public async Task CountAsync_Filters_CountsOnlyTheMatchingMessages(DeliveryStatus? status, bool includeTest, int expected)
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await AddAsync(Finished(AMessage(T1), DeliveryStatus.Failed, Now));
        await AddAsync(AMessage(T1, flags: MessageFlags.Test));
        await AddAsync(With(AMessage(T1), delivery => delivery with { CreatedAtUtc = Now.AddDays(-2) }));

        // Act
        var counts = await TenantStore(T1).CountAsync(new TrafficQuery(CreatedFromUtc: Now.AddDays(-1), Status: status, IncludeTest: includeTest), Ct);

        // Assert
        Assert.Equal(expected, counts.Value.Sum(static count => count.Count));
    }
}
