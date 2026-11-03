using System.Text.Json.Serialization;

namespace MsgHatch.SignalR;

/// <summary>The JSON type information of the SignalR settings. Names are camelCase (D23).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SignalRChannelSettings))]
internal sealed partial class SignalRJson : JsonSerializerContext;
