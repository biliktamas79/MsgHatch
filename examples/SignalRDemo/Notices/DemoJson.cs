using System.Text.Json.Serialization;
using MsgHatch.Channels.SignalR;

namespace SignalRDemo.Notices;

/// <summary>The JSON type information of the message type of the demo and of its SignalR envelope.</summary>
[JsonSerializable(typeof(DemoNotice))]
[JsonSerializable(typeof(SignalREnvelope<DemoPayload>))]
public sealed partial class DemoJson : JsonSerializerContext;
