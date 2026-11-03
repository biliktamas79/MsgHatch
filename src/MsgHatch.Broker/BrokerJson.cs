using System.Text.Json.Serialization;

namespace MsgHatch.Broker;

/// <summary>The JSON type information of the broker settings. Names are camelCase (D23).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrokerChannelSettings))]
internal sealed partial class BrokerJson : JsonSerializerContext;
