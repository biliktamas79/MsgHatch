using System.Text.Json.Serialization;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TestEvent))]
[JsonSerializable(typeof(OtherTestEvent))]
public sealed partial class TestJsonContext : JsonSerializerContext;
