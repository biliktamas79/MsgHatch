using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class InboxStoreTestsBase<TStore>
{
    [Fact]
    public async Task CompleteAsync_ClaimedMessage_MarksItDoneAddsATryAndEndsTheLease()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var claimed = Assert.Single((await ClaimAsync()).Value.Messages);
        var triedAtUtc = Now.AddSeconds(1);

        // Act
        var error = await Store.CompleteAsync(claimed, triedAtUtc, Ct);

        // Assert
        Assert.Null(error);
        var delivery = (await StoredAsync(message)).Delivery;
        Assert.Equal(
            (DeliveryStatus.Done, 1, (DateTime?)triedAtUtc, (DateTime?)null),
            (delivery.Status, delivery.TryCount, delivery.LastTryAtUtc, delivery.LeaseUntilUtc));
    }

    [Fact]
    public async Task FailAsync_ClaimedMessageWithANextTryTime_KeepsItPendingUntilThatTime()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var claimed = Assert.Single((await ClaimAsync()).Value.Messages);
        var (triedAtUtc, nextTryAtUtc) = (Now.AddSeconds(1), Now.AddMinutes(1));
        const string text = "RateLimited: Ősz-űr \"quoted\" <tag> & more";

        // Act
        var error = await Store.FailAsync(claimed, text, triedAtUtc, nextTryAtUtc, Ct);

        // Assert
        Assert.Null(error);
        var delivery = (await StoredAsync(message)).Delivery;
        Assert.Equal(
            (DeliveryStatus.Pending, 1, (DateTime?)triedAtUtc, nextTryAtUtc, (DateTime?)null, text),
            (delivery.Status, delivery.TryCount, delivery.LastTryAtUtc, delivery.NextTryAtUtc, delivery.LeaseUntilUtc, delivery.LastError));
    }

    [Fact]
    public async Task FailAsync_NoNextTryTime_FailsTheMessagePermanently()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));

        // Act
        await Store.FailAsync(message, "ValidationFailed", Now, nextTryAtUtc: null, Ct);

        // Assert
        Assert.Equal(DeliveryStatus.Failed, (await StoredAsync(message)).Delivery.Status);
    }

    [Fact]
    public async Task FailAsync_SameIdOfAnotherTenant_ChangesOnlyTheMessageOfItsTenant()
    {
        // Arrange
        var ofT1 = await AddAsync(AMessage(T1));
        var ofT2 = await AddAsync(AMessage(T2, id: ofT1.Delivery.Id));

        // Act
        await Store.FailAsync(ofT2, "ValidationFailed", Now, nextTryAtUtc: null, Ct);

        // Assert
        Assert.Equal(
            [DeliveryStatus.Pending, DeliveryStatus.Failed],
            [(await StoredAsync(ofT1)).Delivery.Status, (await StoredAsync(ofT2)).Delivery.Status]);
    }

    [Fact]
    public async Task CompleteAsync_MessageThatIsNotStored_ReturnsNotFound()
    {
        // Act
        var error = await Store.CompleteAsync(AMessage(T1), Now, Ct);

        // Assert
        Assert.Equal(Errors.NotFound, error?.Type);
    }
}
