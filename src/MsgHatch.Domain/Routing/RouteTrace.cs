namespace MsgHatch.Domain.Routing;

/// <summary>
/// The data that tells why a router gave a route (D98). Each router package derives its own trace type, and
/// registers it with <c>MsgHatchBuilder.AddTraceType</c>. A trace holds no address and no payload value.
/// </summary>
public abstract record RouteTrace;
