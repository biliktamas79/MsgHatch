using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Tests.Unit.Delivery;

public sealed class PublishTryUnitTests
{
    private static readonly DateTime _nextTryAtUtc = new(2030, 1, 1, 0, 5, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(PublishOutcome.Succeeded, true, DeliveryStatus.Done)]
    [InlineData(PublishOutcome.Suppressed, true, DeliveryStatus.Done)]
    [InlineData(PublishOutcome.TransientError, true, DeliveryStatus.Pending)]
    [InlineData(PublishOutcome.TransientError, false, DeliveryStatus.Failed)]
    [InlineData(PublishOutcome.PermanentError, true, DeliveryStatus.Failed)]
    [InlineData(PublishOutcome.Expired, true, DeliveryStatus.Failed)]
    public void StatusAfter_Outcome_GivesTheStatusOfTheRecordRule(PublishOutcome outcome, bool hasNextTry, DeliveryStatus expected)
    {
        // Arrange
        var publishTry = APublishTry(outcome);

        // Act
        var status = publishTry.StatusAfter(hasNextTry ? _nextTryAtUtc : null);

        // Assert
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, "text", null)]
    [InlineData("Unavailable", null, "Unavailable")]
    [InlineData("Unavailable", "text", "Unavailable: text")]
    public void ErrorText_ErrorFields_GivesTheLastErrorText(string? errorName, string? errorMessage, string? expected)
    {
        // Arrange
        var publishTry = APublishTry(PublishOutcome.TransientError) with { ErrorName = errorName, ErrorMessage = errorMessage };

        // Act
        var text = publishTry.ErrorText();

        // Assert
        Assert.Equal(expected, text);
    }

    private static PublishTry APublishTry(PublishOutcome outcome) => new(
        Guid.NewGuid(), TryNumber: 1, StartedAtUtc: _nextTryAtUtc, FinishedAtUtc: _nextTryAtUtc, outcome,
        ProviderMessageId: null, ErrorCode: null, ErrorName: null, ErrorMessage: null);
}
