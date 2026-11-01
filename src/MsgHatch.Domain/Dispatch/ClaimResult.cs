namespace MsgHatch.Domain.Dispatch;

/// <summary>The result of one claim of due messages (D103).</summary>
/// <typeparam name="T">The message type: a received message or an outgoing message.</typeparam>
/// <param name="Messages">The claimed messages.</param>
/// <param name="NextDueAtUtc">
/// The earliest <c>NextTryAtUtc</c> of the pending messages after the claim, which includes the lease ends of the
/// claimed messages, or null when no pending message exists.
/// </param>
/// <param name="Down">The down state of the claimed channel, or null when the channel is up (D106).</param>
public sealed record ClaimResult<T>(IReadOnlyList<T> Messages, DateTime? NextDueAtUtc, ChannelDown? Down = null);
