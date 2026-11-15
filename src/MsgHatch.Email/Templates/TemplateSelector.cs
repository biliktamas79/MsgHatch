using MsgHatch.Domain.Failures;
using MsgHatch.Email.Engines;

namespace MsgHatch.Email.Templates;

/// <summary>
/// Selects the template of a request with the fallback of D123 (section 4.10). It tries the template store of the
/// tenant with the scope and with no scope, and then the shared template store with the scope and with no scope. It
/// does this for the language, and then for the default language. The first hit wins. It skips a missing store, and a
/// lookup with a scope for a request with no scope. A hit that each store gives synchronously allocates nothing
/// (section 4.11). Each template engine of <c>MsgHatch.Email</c> and of another package uses it (D205).
/// </summary>
public sealed class TemplateSelector
{
    private readonly ITemplateStore? _tenantStore;
    private readonly ITemplateStore? _sharedStore;
    private readonly string _defaultLanguage;

    /// <summary>Makes the selector of one tenant key.</summary>
    /// <param name="tenantStore">The template store of the tenant, or null for no tenant or no tenant template store.</param>
    /// <param name="sharedStore">The shared template store, or null when none is registered.</param>
    /// <param name="defaultLanguage">The culture name of the default language of the fallback.</param>
    /// <exception cref="ArgumentException">The default language is null, empty, or only white space.</exception>
    public TemplateSelector(ITemplateStore? tenantStore, ITemplateStore? sharedStore, string defaultLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLanguage);
        _tenantStore = tenantStore;
        _sharedStore = sharedStore;
        _defaultLanguage = defaultLanguage;
    }

    /// <summary>Finds the template of one request.</summary>
    /// <param name="request">The message type, the language, and the template scope.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The first template of the fallback, the error of a store, or <see cref="Errors.NotFound"/>.</returns>
    public async ValueTask<ErrorOr<TemplateSource>> FindAsync(EmailTemplateRequest request, CancellationToken ct)
    {
        var source = await FindInLanguageAsync(request, request.Language, ct);
        if (IsMissing(source) && !IsDefaultLanguage(request.Language))
        {
            source = await FindInLanguageAsync(request, _defaultLanguage, ct);
        }

        if (source.IsError)
        {
            return source.Error;
        }

        return source.Value is { } found
            ? found
            : new Error(Errors.NotFound, $"No template of {request.Type.Name} exists in the language {request.Language} or {_defaultLanguage}.");
    }

    private async ValueTask<ErrorOr<TemplateSource?>> FindInLanguageAsync(EmailTemplateRequest request, string language, CancellationToken ct)
    {
        var source = await FindInStoreAsync(_tenantStore, request, language, ct);
        return IsMissing(source) ? await FindInStoreAsync(_sharedStore, request, language, ct) : source;
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

    private bool IsDefaultLanguage(string language) => string.Equals(language, _defaultLanguage, StringComparison.OrdinalIgnoreCase);
}
