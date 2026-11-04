using System.Text.Json.Serialization;

namespace MsgHatch.Subscriptions.Resolution;

/// <summary>
/// The JSON type information of <see cref="ResolutionTrace"/>, the trace type <c>Trace:Subscription</c> (D98). Names are
/// camelCase and enum values are strings (D23).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, Converters = [typeof(SubscriptionOwnerJsonConverter)])]
[JsonSerializable(typeof(ResolutionTrace))]
internal sealed partial class SubscriptionJson : JsonSerializerContext;
