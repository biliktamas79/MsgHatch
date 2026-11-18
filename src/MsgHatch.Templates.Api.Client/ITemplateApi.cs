using MsgHatch.Domain.Failures;
using MsgHatch.Http;
using MsgHatch.Templates.Api.Contracts;

namespace MsgHatch.Templates.Api.Client;

/// <summary>
/// The routes of the template management API (D224, D240, D241, D256). The generator writes <c>TemplateApi</c>, which sends each
/// call and gives each failure as an <see cref="Error"/> (D233, D236). Each route takes the optional query value <c>tenantId</c>.
/// </summary>
[HttpApi(typeof(TemplateEndpointJson))]
internal interface ITemplateApi
{
    [HttpCall("GET", TemplateApiRoutes.Templates)]
    Task<ErrorOr<IReadOnlyList<TemplateEntryResponse>>> GetTemplatesAsync(string? messageType, string? companyId, string? tenantId, CancellationToken ct);

    [HttpCall("GET", TemplateApiRoutes.Parts)]
    Task<ErrorOr<IReadOnlyList<TemplatePartResponse>>> GetTemplatePartsAsync(string? messageType, string? tenantId, CancellationToken ct);

    [HttpCall("GET", TemplateApiRoutes.Template)]
    Task<ErrorOr<TemplateResponse>> GetTemplateAsync(
        string messageType, string language, string part, TemplatePartKind kind, string? companyId, string? tenantId, CancellationToken ct);

    [HttpCall("POST", TemplateApiRoutes.Check)]
    Task<ErrorOr<IReadOnlyList<TemplateIssueResponse>>> CheckTemplateAsync(
        string messageType, string language, string part, TemplatePartKind kind, [HttpBody] TemplateCheckRequest body, string? companyId,
        string? tenantId, CancellationToken ct);

    [HttpCall("POST", TemplateApiRoutes.Preview)]
    Task<ErrorOr<IReadOnlyList<TemplatePreviewResponse>>> PreviewTemplateAsync(
        string messageType, string language, string part, TemplatePartKind kind, [HttpBody] TemplatePreviewRequest body, string? companyId,
        string? tenantId, CancellationToken ct);

    [HttpCall("PUT", TemplateApiRoutes.Template)]
    Task<ErrorOr<TemplateEntryResponse>> SaveTemplateAsync(
        string messageType, string language, string part, TemplatePartKind kind, [HttpBody] TemplateSaveRequest body, string? companyId,
        string? tenantId, CancellationToken ct);

    [HttpCall("DELETE", TemplateApiRoutes.Template)]
    Task<Error?> DeleteTemplateAsync(
        string messageType, string language, string part, TemplatePartKind kind, string? companyId, string? tenantId, CancellationToken ct);
}
