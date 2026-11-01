using System.Text.Json.Serialization;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TestEvent))]
[JsonSerializable(typeof(TestEventV1))]
[JsonSerializable(typeof(OtherTestEvent))]
[JsonSerializable(typeof(TestEnvelope))]
[JsonSerializable(typeof(TestTrace))]
[JsonSerializable(typeof(TestChannelSettings))]
[JsonSerializable(typeof(OtherTestEnvelope))]
[JsonSerializable(typeof(OtherTestChannelSettings))]
public sealed partial class TestJsonContext : JsonSerializerContext;
