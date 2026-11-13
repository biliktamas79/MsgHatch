using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>The detail of an outgoing message, with no envelope (D109).</summary>
/// <param name="Summary">The summary.</param>
/// <param name="Trace">The trace of the route that gave the message, or null.</param>
/// <param name="Tries">The publish tries, in the order of their numbers.</param>
public sealed record OutgoingMessageDetail(OutgoingMessageSummary Summary, RouteTrace? Trace, IReadOnlyList<PublishTry> Tries);
