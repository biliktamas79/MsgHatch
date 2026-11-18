using System.Text.Json.Serialization;

namespace MsgHatch.Subscriptions.Api.Contracts;

/// <summary>
/// The JSON type information of the request and response bodies of the subscription endpoints, with camelCase names and
/// enum values as strings (section 4.17). A missing required property is invalid JSON, so the endpoint gives 400 for it.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SubscriptionRequest))]
[JsonSerializable(typeof(SubscriptionResponse))]
[JsonSerializable(typeof(IReadOnlyList<SubscriptionResponse>))]
[JsonSerializable(typeof(IReadOnlyList<MessageTypeResponse>))]
[JsonSerializable(typeof(IReadOnlyList<ChannelResponse>))]
[JsonSerializable(typeof(RoutingVerificationBody))]
[JsonSerializable(typeof(RoutingVerificationResponse))]
public sealed partial class SubscriptionEndpointJson : JsonSerializerContext;
