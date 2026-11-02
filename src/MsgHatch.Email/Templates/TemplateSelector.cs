using MsgHatch.Domain.Failures;
using MsgHatch.Email.Engines;

namespace MsgHatch.Email.Templates;

/// <summary>
/// Selects the template of a request with the fallback of D123 (section 4.10). It tries the template store of the
/// tenant with the scope and with no scope, and then the shared template store with the scope and with no scope. It
/// does this for the language, and then for the default language. The first hit wins. It skips a missing store, and a
/// lookup with a scope for a request with no scope.
/// </summary>
internal sealed class TemplateSelector(ITemplateStore? tenantStore, ITemplateStore? sharedStore, string defaultLanguage)
{
    public async ValueTask<ErrorOr<TemplateSource>> FindAsync(EmailTemplateRequest request, CancellationToken ct)
    {
        foreach (var (store, lookup) in LookupsOf(request))
        {
            var source = await store.FindAsync(lookup, ct);
            if (source.IsError)
            {
                return source.Error;
            }

            if (source.Value is not null)
            {
                return source.Value;
            }
        }

        return new Error(Errors.NotFound, $"No template of {request.Type.Name} exists in the language {request.Language} or {defaultLanguage}.");
    }

    private IEnumerable<(ITemplateStore Store, TemplateLookup Lookup)> LookupsOf(EmailTemplateRequest request)
    {
        foreach (var language in LanguagesOf(request.Language))
        {
            foreach (var store in new[] { tenantStore, sharedStore }.OfType<ITemplateStore>())
            {
                if (request.Scope is not null)
                {
                    yield return (store, new TemplateLookup(request.Type, language, request.Scope));
                }

                yield return (store, new TemplateLookup(request.Type, language, Scope: null));
            }
        }
    }

    private string[] LanguagesOf(string language) =>
        string.Equals(language, defaultLanguage, StringComparison.OrdinalIgnoreCase) ? [language] : [language, defaultLanguage];
}
