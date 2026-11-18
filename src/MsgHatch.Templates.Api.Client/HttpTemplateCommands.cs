using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Api.Contracts;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.Api.Client;

/// <summary>
/// The template commands of one tenant key over HTTP (D227, D231). Each method runs <see cref="TemplateLookupRules"/> first,
/// so a request that breaks a rule gives its error with no HTTP call (D234).
/// </summary>
/// <param name="api">The generated client of the routes.</param>
/// <param name="key">The tenant key, whose tenant each request sends as <c>tenantId</c>.</param>
internal sealed class HttpTemplateCommands(TemplateApi api, TenantKey key) : ITemplateCommands
{
    private string? Tenant => key.Tenant?.Value;

    public async ValueTask<ErrorOr<TemplateEntry>> SaveTemplateAsync(TemplateLookup lookup, string text, long expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (TemplateLookupRules.Check(lookup) ?? TemplateLookupRules.CheckText(text)) is { } invalid
            ? invalid
            : ApiResults.One(
                await api.SaveTemplateAsync(
                    lookup.Type.Name, lookup.Language, lookup.Part, lookup.Kind, new TemplateSaveRequest(text, expectedVersion), lookup.Scope, Tenant, ct),
                static entry => entry.ToEntry());
    }

    public async ValueTask<Error?> DeleteTemplateAsync(TemplateLookup lookup, CancellationToken ct) =>
        TemplateLookupRules.Check(lookup)
        ?? await api.DeleteTemplateAsync(lookup.Type.Name, lookup.Language, lookup.Part, lookup.Kind, lookup.Scope, Tenant, ct);
}
