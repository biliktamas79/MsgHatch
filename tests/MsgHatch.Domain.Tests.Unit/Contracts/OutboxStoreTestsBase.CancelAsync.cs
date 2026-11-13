using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
{
    [Theory]
    [MemberData(nameof(ManagementTestRows.CancelStatusRows), MemberType = typeof(ManagementTestRows))]
    public async Task CancelAsync_Status_CancelsOnlyAPendingMessage(DeliveryStatus status, int expected, DeliveryStatus expectedStatus)
    {
        // Arrange
        var message = await AddAsync(With(AMessage(T1), delivery => delivery with { Status = status }));

        // Act
        var changed = await TenantStore(T1).CancelAsync([message.Delivery.Id], Now, Ct);

        // Assert
        Assert.Equal((expected, expectedStatus), (changed.Value, (await StoredAsync(message.Delivery.Id)).Delivery.Status));
    }

    [Fact]
    public async Task CancelAsync_PendingMessageWithATry_RecordsACancelledTryAndKeepsTheLastError()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var first = ATry(message, 1, PublishOutcome.TransientError, errorMessage: "Too many");
        await Store.RecordTryAsync(message, first, Now.AddMinutes(1), Ct);
        Time.Advance(TimeSpan.FromSeconds(10));

        // Act
        await TenantStore(T1).CancelAsync([message.Delivery.Id], Now, Ct);

        // Assert
        var delivery = (await StoredAsync(message.Delivery.Id)).Delivery;
        var cancelled = new PublishTry(message.Delivery.Id, 2, Now, Now, PublishOutcome.Cancelled, null, null, null, null);
        Assert.Equal(
            (DeliveryStatus.Cancelled, 2, (DateTime?)Now, "RateLimited: Too many"),
            (delivery.Status, delivery.TryCount, delivery.LastTryAtUtc, delivery.LastError));
        Assert.Equal([first, cancelled], await TestData.GetTriesAsync(message.Delivery.Id, Ct));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task CancelAsync_LeaseEnd_CancelsThePendingMessageOnlyWhenTheLeaseEnded(int leaseEndSeconds, int expected)
    {
        // Arrange
        var leaseUntilUtc = Now.AddSeconds(leaseEndSeconds);
        var message = await AddAsync(With(AMessage(T1, nextTryAtUtc: leaseUntilUtc), delivery => delivery with { LeaseUntilUtc = leaseUntilUtc }));

        // Act
        var changed = await TenantStore(T1).CancelAsync([message.Delivery.Id], Now, Ct);

        // Assert
        Assert.Equal(expected, changed.Value);
    }

    [Fact]
    public async Task CancelAsync_CancelledMessage_IsNeverClaimedAgain()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        await TenantStore(T1).CancelAsync([message.Delivery.Id], Now, Ct);
        Time.Advance(TimeSpan.FromDays(1));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)null), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task RecordTryAsync_TryOfAClaimAfterACancel_KeepsTheCancelledStatus()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var claimed = Assert.Single((await ClaimAsync()).Value.Messages);
        Time.Advance(TimeSpan.FromSeconds(31));
        await TenantStore(T1).CancelAsync([message.Delivery.Id], Now, Ct);

        // Act
        await Store.RecordTryAsync(claimed, ATry(message, 1, PublishOutcome.Succeeded), nextTryAtUtc: null, Ct);

        // Assert
        Assert.Equal(DeliveryStatus.Cancelled, (await StoredAsync(message.Delivery.Id)).Delivery.Status);
    }

    [Fact]
    public async Task CancelAsync_UnknownIdAndIdOfAnotherTenant_ChangesNothing()
    {
        // Arrange
        var ofT2 = await AddAsync(AMessage(T2));

        // Act
        var changed = await TenantStore(T1).CancelAsync([Guid.NewGuid(), ofT2.Delivery.Id], Now, Ct);

        // Assert
        Assert.Equal((0, DeliveryStatus.Pending), (changed.Value, (await StoredAsync(ofT2.Delivery.Id)).Delivery.Status));
    }
}
