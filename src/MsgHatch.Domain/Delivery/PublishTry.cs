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
    PublishOutcome Outcome, string? ProviderMessageId, int? ErrorCode, string? ErrorName, string? ErrorMessage);
