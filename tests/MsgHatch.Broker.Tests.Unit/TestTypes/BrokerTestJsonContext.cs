using System.Text.Json.Serialization;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Broker.Tests.Unit.TestTypes;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrokerEnvelope<TestPayload>))]
public sealed partial class BrokerTestJsonContext : JsonSerializerContext;
