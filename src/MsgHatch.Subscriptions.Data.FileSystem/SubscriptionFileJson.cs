using System.Text.Json.Serialization;

namespace MsgHatch.Subscriptions.Data.FileSystem;

/// <summary>
/// The JSON type information of a subscription file, with camelCase names and enum values as strings (D23). A missing
/// property or a null value where the type allows none is invalid JSON, so the store gives
/// <c>Errors.SerializationFailed</c> for it.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, WriteIndented = true,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SubscriptionFile))]
internal sealed partial class SubscriptionFileJson : JsonSerializerContext;
