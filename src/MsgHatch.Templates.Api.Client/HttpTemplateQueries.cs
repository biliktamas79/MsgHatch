using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Api.Contracts;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.Api.Client;

/// <summary>
/// The template queries of one tenant key over HTTP (D227, D231). Each method runs <see cref="TemplateLookupRules"/> first,
/// so a request that breaks a rule gives its error with no HTTP call (D234). The API has no template id (D224), so the id of
/// the key of a read template is a stable id of its lookup, which a caller reads as opaque.
/// </summary>
/// <param name="api">The generated client of the routes.</param>
/// <param name="key">The tenant key, whose tenant each request sends as <c>tenantId</c>.</param>
internal sealed class HttpTemplateQueries(TemplateApi api, TenantKey key) : ITemplateQueries
{
    private string? Tenant => key.Tenant?.Value;

    public async ValueTask<ErrorOr<IReadOnlyList<TemplateEntry>>> GetTemplatesAsync(TemplateQuery query, CancellationToken ct) =>
        TemplateLookupRules.Check(query) is { } invalid
            ? invalid
            : ApiResults.All(await api.GetTemplatesAsync(query.Type?.Name, query.Scope, Tenant, ct), static entry => entry.ToEntry());

    public async ValueTask<ErrorOr<TemplateSource>> GetTemplateAsync(TemplateLookup lookup, CancellationToken ct) =>
        TemplateLookupRules.Check(lookup) is { } invalid
            ? invalid
            : ApiResults.One(
                await api.GetTemplateAsync(lookup.Type.Name, lookup.Language, lookup.Part, lookup.Kind, lookup.Scope, Tenant, ct),
                static template => template.ToSource());

    public async ValueTask<ErrorOr<IReadOnlyList<TemplatePartInfo>>> GetTemplatePartsAsync(MessageType? type, CancellationToken ct) =>
        ApiResults.All(await api.GetTemplatePartsAsync(type?.Name, Tenant, ct), static part => part.ToPartInfo());

    public async ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> CheckTemplateAsync(TemplateLookup lookup, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (TemplateLookupRules.Check(lookup) ?? TemplateLookupRules.CheckText(text)) is { } invalid
            ? invalid
            : ApiResults.All(
                await api.CheckTemplateAsync(
                    lookup.Type.Name, lookup.Language, lookup.Part, lookup.Kind, new TemplateCheckRequest(text), lookup.Scope, Tenant, ct),
                static issue => issue.ToIssue());
    }

    public async ValueTask<ErrorOr<IReadOnlyList<TemplatePreview>>> PreviewTemplateAsync(TemplateLookup lookup, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (TemplateLookupRules.Check(lookup) ?? TemplateLookupRules.CheckText(text)) is { } invalid
            ? invalid
            : ApiResults.All(
                await api.PreviewTemplateAsync(
                    lookup.Type.Name, lookup.Language, lookup.Part, lookup.Kind, new TemplatePreviewRequest(text), lookup.Scope, Tenant, ct),
                static preview => preview.ToPreview());
    }
}
