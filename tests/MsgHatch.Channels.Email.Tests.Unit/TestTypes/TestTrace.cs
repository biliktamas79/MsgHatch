using MsgHatch.Domain.Routing;

namespace MsgHatch.Channels.Email.Tests.Unit.TestTypes;

/// <summary>The route trace of the email tests.</summary>
public sealed record TestTrace(string Router) : RouteTrace;
