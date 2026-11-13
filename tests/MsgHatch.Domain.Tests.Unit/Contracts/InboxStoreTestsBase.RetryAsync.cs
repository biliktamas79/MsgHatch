using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class InboxStoreTestsBase<TStore>
{
    [Theory]
    [MemberData(nameof(ManagementTestRows.RetryStatusRows), MemberType = typeof(ManagementTestRows))]
    public async Task RetryAsync_Status_ChangesOnlyAFailedOrPendingMessage(DeliveryStatus status, int expected, DeliveryStatus expectedStatus)
    {
        // Arrange
        var message = await AddAsync(With(AMessage(T1, nextTryAtUtc: Now.AddHours(1)), delivery => delivery with { Status = status }));

        // Act
        var changed = await TenantStore(T1).RetryAsync([message.Delivery.Id], Now, addedTries: 3, Ct);

        // Assert
        Assert.Equal((expected, expectedStatus), (changed.Value, (await StoredAsync(message)).Delivery.Status));
    }

    [Fact]
    public async Task RetryAsync_FailedMessage_GivesItANewSetOfTriesDueNowAndKeepsItsHistory()
    {
        // Arrange
        var message = await AddAsync(With(AMessage(T1, nextTryAtUtc: Now.AddHours(-1)), delivery => delivery with
        {
            Status = DeliveryStatus.Failed, TryCount = 8, MaxTryCount = 8, LastTryAtUtc = Now.AddHours(-1), LastError = "Unavailable: down",
        }));

        // Act
        await TenantStore(T1).RetryAsync([message.Delivery.Id], Now, addedTries: 3, Ct);

        // Assert
        var delivery = (await StoredAsync(message)).Delivery;
        Assert.Equal(
            (DeliveryStatus.Pending, 8, 11, Now, (DateTime?)Now.AddHours(-1), "Unavailable: down"),
            (delivery.Status, delivery.TryCount, delivery.MaxTryCount, delivery.NextTryAtUtc, delivery.LastTryAtUtc, delivery.LastError));
    }

    [Fact]
    public async Task RetryAsync_PendingMessage_MakesItDueNowAndKeepsItsMaxTryCount()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddHours(1)));

        // Act
        await TenantStore(T1).RetryAsync([message.Delivery.Id], Now, addedTries: 3, Ct);

        // Assert
        var delivery = (await StoredAsync(message)).Delivery;
        Assert.Equal((Now, 8), (delivery.NextTryAtUtc, delivery.MaxTryCount));
    }

    [Fact]
    public async Task RetryAsync_FailedMessage_IsClaimedAgain()
    {
        // Arrange
        var message = await AddAsync(With(AMessage(T1), static delivery => delivery with { Status = DeliveryStatus.Failed }));
        await TenantStore(T1).RetryAsync([message.Delivery.Id], Now, addedTries: 3, Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task RetryAsync_LeaseEnd_ChangesThePendingMessageOnlyWhenTheLeaseEnded(int leaseEndSeconds, int expected)
    {
        // Arrange
        var leaseUntilUtc = Now.AddSeconds(leaseEndSeconds);
        var message = await AddAsync(With(AMessage(T1, nextTryAtUtc: leaseUntilUtc), delivery => delivery with { LeaseUntilUtc = leaseUntilUtc }));

        // Act
        var changed = await TenantStore(T1).RetryAsync([message.Delivery.Id], Now, addedTries: 3, Ct);

        // Assert
        Assert.Equal(expected, changed.Value);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public async Task RetryAsync_Expiry_ChangesTheFailedMessageOnlyBeforeItsExpiry(int expirySeconds, int expected)
    {
        // Arrange
        var message = await AddAsync(With(AMessage(T1), delivery => delivery with
        {
            Status = DeliveryStatus.Failed, ExpiresAtUtc = Now.AddSeconds(expirySeconds),
        }));

        // Act
        var changed = await TenantStore(T1).RetryAsync([message.Delivery.Id], Now, addedTries: 3, Ct);

        // Assert
        Assert.Equal(expected, changed.Value);
    }

    [Fact]
    public async Task RetryAsync_UnknownIdAndIdOfAnotherTenant_ChangesNothing()
    {
        // Arrange
        var ofT2 = await AddAsync(With(AMessage(T2), static delivery => delivery with { Status = DeliveryStatus.Failed }));

        // Act
        var changed = await TenantStore(T1).RetryAsync([Guid.NewGuid(), ofT2.Delivery.Id], Now, addedTries: 3, Ct);

        // Assert
        Assert.Equal((0, DeliveryStatus.Failed), (changed.Value, (await StoredAsync(ofT2)).Delivery.Status));
    }
}
