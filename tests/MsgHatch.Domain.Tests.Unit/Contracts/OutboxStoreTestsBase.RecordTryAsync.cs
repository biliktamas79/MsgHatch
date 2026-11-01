using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
{
    [Theory]
    [MemberData(nameof(OutboxStoreTestRows.OutcomeRows), MemberType = typeof(OutboxStoreTestRows))]
    public async Task RecordTryAsync_Outcome_GivesTheStatusOfTheOutcome(PublishOutcome outcome, bool withNextTry, DeliveryStatus expected)
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        DateTime? nextTryAtUtc = withNextTry ? Now.AddMinutes(1) : null;

        // Act
        var error = await Store.RecordTryAsync(message, ATry(message, 1, outcome), nextTryAtUtc, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(expected, (await StoredAsync(message.Delivery.Id)).Delivery.Status);
    }

    [Fact]
    public async Task RecordTryAsync_TransientErrorOfAClaimedMessage_SetsTheDeliveryStateAndEndsTheLease()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var claimed = Assert.Single((await ClaimAsync()).Value.Messages);
        var publishTry = ATry(message, 1, PublishOutcome.TransientError, errorMessage: "Too many");
        var nextTryAtUtc = Now.AddMinutes(1);

        // Act
        await Store.RecordTryAsync(claimed, publishTry, nextTryAtUtc, Ct);

        // Assert
        var delivery = (await StoredAsync(message.Delivery.Id)).Delivery;
        Assert.Equal(
            (DeliveryStatus.Pending, 1, (DateTime?)publishTry.FinishedAtUtc, nextTryAtUtc, (DateTime?)null, "RateLimited: Too many"),
            (delivery.Status, delivery.TryCount, delivery.LastTryAtUtc, delivery.NextTryAtUtc, delivery.LeaseUntilUtc, delivery.LastError));
    }

    [Fact]
    public async Task RecordTryAsync_SuccessAfterATransientError_SetsNoLastError()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        await Store.RecordTryAsync(message, ATry(message, 1, PublishOutcome.TransientError), Now.AddMinutes(1), Ct);

        // Act
        await Store.RecordTryAsync(message, ATry(message, 2, PublishOutcome.Succeeded), nextTryAtUtc: null, Ct);

        // Assert
        var delivery = (await StoredAsync(message.Delivery.Id)).Delivery;
        Assert.Equal((DeliveryStatus.Done, 2, (string?)null), (delivery.Status, delivery.TryCount, delivery.LastError));
    }

    [Fact]
    public async Task RecordTryAsync_ThreeTries_KeepsAllTriesInOrderWithTheirTextsUnchanged()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        PublishTry[] tries =
        [
            ATry(message, 1, PublishOutcome.TransientError, errorMessage: "Ősz-űr \"quoted\" back\\slash <tag> & more"),
            ATry(message, 2, PublishOutcome.TransientError),
            ATry(message, 3, PublishOutcome.Succeeded),
        ];
        await Store.RecordTryAsync(message, tries[0], Now.AddMinutes(1), Ct);
        await Store.RecordTryAsync(message, tries[1], Now.AddMinutes(2), Ct);

        // Act
        await Store.RecordTryAsync(message, tries[2], nextTryAtUtc: null, Ct);

        // Assert
        Assert.Equal(tries, await TestData.GetTriesAsync(message.Delivery.Id, Ct));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task RecordTryAsync_TryNumberThatIsNotTheStoredTryCountPlusOne_ChangesNothing(int tryNumber)
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var first = ATry(message, 1, PublishOutcome.TransientError);
        await Store.RecordTryAsync(message, first, Now.AddMinutes(1), Ct);
        var stored = await StoredAsync(message.Delivery.Id);

        // Act
        var error = await Store.RecordTryAsync(message, ATry(message, tryNumber, PublishOutcome.Succeeded), nextTryAtUtc: null, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(stored, await StoredAsync(message.Delivery.Id));
        Assert.Equal([first], await TestData.GetTriesAsync(message.Delivery.Id, Ct));
    }

    [Fact]
    public async Task RecordTryAsync_MessageThatIsNotStored_ReturnsNotFound()
    {
        // Arrange
        var message = AMessage(T1);

        // Act
        var error = await Store.RecordTryAsync(message, ATry(message, 1, PublishOutcome.Succeeded), nextTryAtUtc: null, Ct);

        // Assert
        Assert.Equal(Errors.NotFound, error?.Type);
    }

    private PublishTry ATry(OutgoingMessage message, int tryNumber, PublishOutcome outcome, string? errorMessage = null)
    {
        var isError = outcome is PublishOutcome.TransientError or PublishOutcome.PermanentError or PublishOutcome.Expired;
        return new PublishTry(
            message.Delivery.Id, tryNumber, Now, Now.AddSeconds(1), outcome,
            ProviderMessageId: outcome == PublishOutcome.Succeeded ? "provider-1" : null,
            ErrorCode: isError ? 429 : null, ErrorName: isError ? "RateLimited" : null, errorMessage);
    }
}
