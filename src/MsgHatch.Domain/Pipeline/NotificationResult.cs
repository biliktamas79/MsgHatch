namespace MsgHatch.Domain.Pipeline;

/// <summary>The result of the processing of one event.</summary>
/// <param name="Stored">The number of stored outgoing messages.</param>
/// <param name="Failed">The number of failed routes.</param>
/// <param name="Errors">The error of each failed route, in the order of the routes. It is empty when no route failed.</param>
public sealed record NotificationResult(int Stored, int Failed, IReadOnlyList<Failures.Error> Errors);
