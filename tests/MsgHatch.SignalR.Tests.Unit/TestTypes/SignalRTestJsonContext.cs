using System.Text.Json.Serialization;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.SignalR.Tests.Unit.TestTypes;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SignalREnvelope<TestPayload>))]
public sealed partial class SignalRTestJsonContext : JsonSerializerContext;
