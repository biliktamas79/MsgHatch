using System.Text.Json.Serialization.Metadata;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Broker;

/// <summary>One publish of a broker message, which the broker transport gives to <see cref="IBrokerClient"/> (section 4.6).</summary>
/// <param name="MessageId">The id of the outgoing message.</param>
/// <param name="Tenant">The tenant, or null for no tenant.</param>
/// <param name="ContentType">
/// The content type of the outgoing message, for example <c>Broker:DEvent</c>. A direct client selects its destination
/// with it (D299).
/// </param>
/// <param name="Payload">The payload, which the client publishes with its run-time type.</param>
/// <param name="PayloadJson">
/// The JSON type information of the run-time type of the payload, from the JSON options of the envelope. A direct client
/// writes the payload with it (D299).
/// </param>
/// <param name="Headers">All headers of the message: the headers of the channel settings and the headers of the library.</param>
public readonly record struct BrokerPublish(
    Guid MessageId, TenantId? Tenant, string ContentType, object Payload, JsonTypeInfo PayloadJson, IReadOnlyDictionary<string, string> Headers);
