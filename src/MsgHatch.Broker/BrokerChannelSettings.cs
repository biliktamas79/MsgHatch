using MsgHatch.Domain.Routing;

namespace MsgHatch.Broker;

/// <summary>The settings of a broker route.</summary>
/// <param name="Headers">
/// The headers that each broker message of the route carries, or null for none. A name never starts with
/// <c>msghatch-</c>, because the library uses these names.
/// </param>
public sealed record BrokerChannelSettings(IReadOnlyDictionary<string, string>? Headers = null) : ChannelSettings;
