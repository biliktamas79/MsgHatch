using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>The route trace of the test router.</summary>
public sealed record TestTrace(string Reason) : RouteTrace;
