using System.Text.Json.Serialization;
using MsgHatch.Broker;
using MsgHatch.SignalR;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>The JSON type information of the test events and the test envelopes.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventA))]
[JsonSerializable(typeof(EventB))]
[JsonSerializable(typeof(EventC))]
[JsonSerializable(typeof(EventD))]
[JsonSerializable(typeof(EventE))]
[JsonSerializable(typeof(EventG))]
[JsonSerializable(typeof(Notice))]
[JsonSerializable(typeof(SignalREnvelope<Notice>))]
[JsonSerializable(typeof(BrokerEnvelope<Notice>))]
internal sealed partial class SubscriptionTestJson : JsonSerializerContext;
