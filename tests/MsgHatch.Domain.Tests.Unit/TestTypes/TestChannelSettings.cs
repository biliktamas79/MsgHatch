using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

public sealed record TestChannelSettings(string? Language = null) : ChannelSettings;
