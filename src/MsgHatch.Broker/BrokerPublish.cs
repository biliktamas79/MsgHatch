using MsgHatch.Domain.Identity;

namespace MsgHatch.Broker;

/// <summary>One publish of a broker message, which the broker transport gives to <see cref="IBrokerClient"/> (section 4.6).</summary>
/// <param name="MessageId">The id of the outgoing message.</param>
/// <param name="Tenant">The tenant, or null for no tenant.</param>
/// <param name="Payload">The payload, which the client publishes with its run-time type.</param>
/// <param name="Headers">All headers of the message: the headers of the channel settings and the headers of the library.</param>
public readonly record struct BrokerPublish(Guid MessageId, TenantId? Tenant, object Payload, IReadOnlyDictionary<string, string> Headers);
