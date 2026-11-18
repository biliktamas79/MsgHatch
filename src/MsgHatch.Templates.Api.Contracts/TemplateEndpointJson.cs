using System.Text.Json.Serialization;

namespace MsgHatch.Templates.Api.Contracts;

/// <summary>
/// The JSON type information of the request and response bodies of the template endpoints, with camelCase names and enum
/// values as strings (section 4.22, D156). A missing required property, and a null value of a property that is not
/// nullable, are invalid JSON, so the endpoint gives 400 for them.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(TemplateSaveRequest))]
[JsonSerializable(typeof(TemplateEntryResponse))]
[JsonSerializable(typeof(TemplateResponse))]
[JsonSerializable(typeof(IReadOnlyList<TemplateEntryResponse>))]
[JsonSerializable(typeof(TemplateCheckRequest))]
[JsonSerializable(typeof(IReadOnlyList<TemplateIssueResponse>))]
[JsonSerializable(typeof(IReadOnlyList<TemplatePartResponse>))]
public sealed partial class TemplateEndpointJson : JsonSerializerContext;
