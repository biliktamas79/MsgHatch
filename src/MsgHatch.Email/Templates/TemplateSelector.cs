using MsgHatch.Domain.Failures;
using MsgHatch.Email.Engines;

namespace MsgHatch.Email.Templates;

/// <summary>
/// Selects the template of a request with the fallback of D123 (section 4.10). It tries the template store of the
/// tenant with the scope and with no scope, and then the shared template store with the scope and with no scope. It
/// does this for the language, and then for the default language. The first hit wins. It skips a missing store, and a
/// lookup with a scope for a request with no scope. A hit that each store gives synchronously allocates nothing
/// (section 4.11).
/// </summary>
internal sealed class TemplateSelector(ITemplateStore? tenantStore, ITemplateStore? sharedStore, string defaultLanguage)
{
    public async ValueTask<ErrorOr<TemplateSource>> FindAsync(EmailTemplateRequest request, CancellationToken ct)
    {
        var source = await FindInLanguageAsync(request, request.Language, ct);
        if (IsMissing(source) && !IsDefaultLanguage(request.Language))
        {
            source = await FindInLanguageAsync(request, defaultLanguage, ct);
        }

        if (source.IsError)
        {
            return source.Error;
        }

        return source.Value is { } found
            ? found
            : new Error(Errors.NotFound, $"No template of {request.Type.Name} exists in the language {request.Language} or {defaultLanguage}.");
    }

    private async ValueTask<ErrorOr<TemplateSource?>> FindInLanguageAsync(EmailTemplateRequest request, string language, CancellationToken ct)
    {
        var source = await FindInStoreAsync(tenantStore, request, language, ct);
        return IsMissing(source) ? await FindInStoreAsync(sharedStore, request, language, ct) : source;
    }

    private static async ValueTask<ErrorOr<TemplateSource?>> FindInStoreAsync(
        ITemplateStore? store, EmailTemplateRequest request, string language, CancellationToken ct)
    {
        if (store is null)
        {
            return ErrorOr<TemplateSource?>.ForValue(null);
        }

        var scoped = request.Scope is null
            ? ErrorOr<TemplateSource?>.ForValue(null)
            : await store.FindAsync(new TemplateLookup(request.Type, language, request.Scope), ct);
        return IsMissing(scoped) ? await store.FindAsync(new TemplateLookup(request.Type, language, Scope: null), ct) : scoped;
    }

    private static bool IsMissing(ErrorOr<TemplateSource?> source) => source.IsSuccess && source.Value is null;

    private bool IsDefaultLanguage(string language) => string.Equals(language, defaultLanguage, StringComparison.OrdinalIgnoreCase);
}
