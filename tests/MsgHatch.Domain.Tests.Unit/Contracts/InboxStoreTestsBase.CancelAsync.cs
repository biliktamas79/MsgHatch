using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class InboxStoreTestsBase<TStore>
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
        Assert.Equal((expected, expectedStatus), (changed.Value, (await StoredAsync(message)).Delivery.Status));
    }

    [Fact]
    public async Task CancelAsync_PendingMessage_KeepsItsTriesAndItsLastError()
    {
        // Arrange
        const string lastError = "Unavailable: down";
        var lastTryAtUtc = Now.AddMinutes(-1);
        var message = await AddAsync(With(AMessage(T1), delivery => delivery with
        {
            TryCount = 2, LastTryAtUtc = lastTryAtUtc, LastError = lastError,
        }));

        // Act
        await TenantStore(T1).CancelAsync([message.Delivery.Id], Now, Ct);

        // Assert
        var delivery = (await StoredAsync(message)).Delivery;
        Assert.Equal(
            (DeliveryStatus.Cancelled, 2, (DateTime?)lastTryAtUtc, lastError),
            (delivery.Status, delivery.TryCount, delivery.LastTryAtUtc, delivery.LastError));
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
    public async Task CancelAsync_UnknownIdAndIdOfAnotherTenant_ChangesNothing()
    {
        // Arrange
        var ofT2 = await AddAsync(AMessage(T2));

        // Act
        var changed = await TenantStore(T1).CancelAsync([Guid.NewGuid(), ofT2.Delivery.Id], Now, Ct);

        // Assert
        Assert.Equal((0, DeliveryStatus.Pending), (changed.Value, (await StoredAsync(ofT2)).Delivery.Status));
    }
}
