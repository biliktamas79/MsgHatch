using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Pipeline;

/// <summary>The result of one route of a preview (D84, D100): the outgoing message, or the error of the route.</summary>
/// <param name="Route">The route.</param>
/// <param name="Message">The outgoing message that the processing stores for the route, or null for an error.</param>
/// <param name="Error">The error of the route, or null.</param>
public readonly record struct RouteResult(MessageRoute Route, OutgoingMessage? Message, Error? Error);
