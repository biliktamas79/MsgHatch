namespace MsgHatch.Domain.Delivery;

/// <summary>One try to publish an outgoing message, with its outcome. All times are UTC.</summary>
/// <param name="OutgoingMessageId">The id of the outgoing message.</param>
/// <param name="TryNumber">The number of the try. The first try has the number 1.</param>
/// <param name="StartedAtUtc">The start time of the try.</param>
/// <param name="FinishedAtUtc">The finish time of the try.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="ProviderMessageId">The message id of the provider for a success, or null.</param>
/// <param name="ErrorCode">The code of the error type, or null.</param>
/// <param name="ErrorName">The name of the error type, or null.</param>
/// <param name="ErrorMessage">The text of the error, or null.</param>
public sealed record PublishTry(
    Guid OutgoingMessageId, int TryNumber, DateTime StartedAtUtc, DateTime FinishedAtUtc,
    PublishOutcome Outcome, string? ProviderMessageId, int? ErrorCode, string? ErrorName, string? ErrorMessage)
{
    /// <summary>
    /// Gives the status of the stored message after this try (section 4.5): <see cref="DeliveryStatus.Done"/> for
    /// <see cref="PublishOutcome.Succeeded"/> and <see cref="PublishOutcome.Suppressed"/>,
    /// <see cref="DeliveryStatus.Pending"/> for a <see cref="PublishOutcome.TransientError"/> with a next try time, and
    /// <see cref="DeliveryStatus.Failed"/> for all other outcomes.
    /// </summary>
    /// <param name="nextTryAtUtc">The time of the next try, or null for no next try.</param>
    /// <returns>The status.</returns>
    public DeliveryStatus StatusAfter(DateTime? nextTryAtUtc) => Outcome switch
    {
        PublishOutcome.Succeeded or PublishOutcome.Suppressed => DeliveryStatus.Done,
        PublishOutcome.TransientError when nextTryAtUtc is not null => DeliveryStatus.Pending,
        _ => DeliveryStatus.Failed,
    };

    /// <summary>
    /// Gives the <c>LastError</c> text of this try: <c>{ErrorName}: {ErrorMessage}</c>, only the error name when the try
    /// has no error message, or null when the try has no error.
    /// </summary>
    /// <returns>The text, or null.</returns>
    public string? ErrorText() =>
        ErrorName is null ? null
        : ErrorMessage is null ? ErrorName
        : $"{ErrorName}: {ErrorMessage}";
}
