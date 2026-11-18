using System.Text.Json.Serialization;

namespace MsgHatch.Api.Contracts;

/// <summary>
/// The JSON type information of the request and response bodies of the registry endpoints, with camelCase names and
/// enum values as strings (section 4.20). A missing required property is invalid JSON, so the endpoint gives 400 for it.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ChannelEntryRequest))]
[JsonSerializable(typeof(MessageTypeEntryRequest))]
[JsonSerializable(typeof(IReadOnlyList<RegistryChannelResponse>))]
[JsonSerializable(typeof(IReadOnlyList<RegistryMessageTypeResponse>))]
[JsonSerializable(typeof(IReadOnlyList<ChannelStateResponse>))]
[JsonSerializable(typeof(ChannelEntryResponse))]
[JsonSerializable(typeof(MessageTypeEntryResponse))]
public sealed partial class RegistryEndpointJson : JsonSerializerContext;
