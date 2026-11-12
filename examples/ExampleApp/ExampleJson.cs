using ExampleApp.Notices;
using ExampleApp.PasswordResets;
using System.Text.Json.Serialization;
using MsgHatch.Broker;
using MsgHatch.SignalR;

namespace ExampleApp;

/// <summary>The JSON type information of the example events and of the envelopes that the outbox stores (section 4.13).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventA))]
[JsonSerializable(typeof(EventB))]
[JsonSerializable(typeof(EventC))]
[JsonSerializable(typeof(EventD))]
[JsonSerializable(typeof(EventE))]
[JsonSerializable(typeof(PasswordReset))]
[JsonSerializable(typeof(Notice))]
[JsonSerializable(typeof(SignalREnvelope<Notice>))]
[JsonSerializable(typeof(BrokerEnvelope<Notice>))]
public sealed partial class ExampleJson : JsonSerializerContext;
