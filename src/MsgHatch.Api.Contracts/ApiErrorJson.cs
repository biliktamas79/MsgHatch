using System.Text.Json.Serialization;

namespace MsgHatch.Api.Contracts;

/// <summary>
/// The JSON type information of <see cref="ErrorTypeBody"/>, with camelCase names (D233, D237). A missing required
/// property, and a null value of a property that is not nullable, are invalid JSON.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ErrorTypeBody))]
public sealed partial class ApiErrorJson : JsonSerializerContext;
