using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Management;

/// <summary>The detail of an outgoing message, with no envelope (D109).</summary>
/// <param name="Summary">The summary.</param>
/// <param name="Trace">
/// The trace of the route that gave the message as JSON text, written with the newest registered version of its type, or null
/// (D290).
/// </param>
/// <param name="Tries">The publish tries, in the order of their numbers.</param>
public sealed record OutgoingMessageDetail(OutgoingMessageSummary Summary, string? Trace, IReadOnlyList<PublishTry> Tries);
