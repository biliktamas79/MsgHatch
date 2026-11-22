using System.Text.Json.Serialization;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Broker.Tests.Unit.TestTypes;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrokerEnvelope<TestPayload>))]
[JsonSerializable(typeof(BrokerEnvelope<TestEvent>))]
[JsonSerializable(typeof(BrokerEnvelope<OtherPayload>))]
public sealed partial class BrokerTestJsonContext : JsonSerializerContext;
