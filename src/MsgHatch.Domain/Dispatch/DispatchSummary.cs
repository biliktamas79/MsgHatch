namespace MsgHatch.Domain.Dispatch;

/// <summary>The counts of one batch of a dispatcher.</summary>
/// <param name="Claimed">The number of claimed messages.</param>
/// <param name="Succeeded">The number of messages that are done.</param>
/// <param name="Retried">The number of messages that wait for a next try.</param>
/// <param name="Failed">The number of messages that failed permanently.</param>
public readonly record struct DispatchSummary(int Claimed, int Succeeded, int Retried, int Failed);
