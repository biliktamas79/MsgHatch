using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates;

/// <summary>
/// Selects the template of one part of a request with the fallback of D123 (section 4.10), so each required part falls
/// back on its own (D214). It tries the template store of the tenant with the scope and with no scope, and then the
/// shared template store with the scope and with no scope. It does this for the language, and then for the default
/// language. The first hit wins. It skips a missing store, and a lookup with a scope for a request with no scope. An
/// optional request has no fallback: it tries only its own language and scope, in the store of the tenant and then in
/// the shared store, so an optional part follows the language and scope of its required part (D214). A hit that each
/// store gives synchronously allocates nothing (section 4.11). Each template engine uses it (D205).
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

    /// <summary>
    /// Makes the selector of one tenant key from a container: the keyed template store of the tenant, the shared
    /// template store, and the default language of <see cref="MsgHatchOptions"/>.
    /// </summary>
    /// <param name="tenantServices">The service provider of the tenant services of the key.</param>
    /// <param name="key">The tenant key.</param>
    /// <returns>The selector.</returns>
    public static TemplateSelector For(IServiceProvider tenantServices, TenantKey key)
    {
        ArgumentNullException.ThrowIfNull(tenantServices);
        var services = tenantServices.GetRequiredService<IServiceProvider>();
        return new TemplateSelector(
            key == TenantKey.None ? null : services.GetKeyedService<ITemplateStore>(key),
            services.GetService<ITemplateStore>(),
            services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.DefaultLanguage);
    }

    /// <summary>Finds the template of one request.</summary>
    /// <param name="request">The message type, the language, the template scope, and the part.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The first template of the fallback with its lookup, the error of a store, or <see cref="Errors.NotFound"/>.</returns>
    public async ValueTask<ErrorOr<TemplateMatch>> FindAsync(TemplateRequest request, CancellationToken ct)
    {
        var match = await FindInLanguageAsync(request, request.Language, ct);
        if (IsMissing(match) && !request.Optional && !IsDefaultLanguage(request.Language))
        {
            match = await FindInLanguageAsync(request, _defaultLanguage, ct);
        }

        if (match.IsError)
        {
            return match.Error;
        }

        return match.Value is { } found
            ? found
            : new Error(Errors.NotFound, $"No {request.Part} template of {request.Type.Name} exists in the language {LanguagesOf(request)}.");
    }

    private async ValueTask<ErrorOr<TemplateMatch?>> FindInLanguageAsync(TemplateRequest request, string language, CancellationToken ct)
    {
        var match = await FindInStoreAsync(_tenantStore, request, language, ct);
        return IsMissing(match) ? await FindInStoreAsync(_sharedStore, request, language, ct) : match;
    }

    private static async ValueTask<ErrorOr<TemplateMatch?>> FindInStoreAsync(
        ITemplateStore? store, TemplateRequest request, string language, CancellationToken ct)
    {
        if (store is null)
        {
            return ErrorOr<TemplateMatch?>.ForValue(null);
        }

        var scoped = request.Scope is null
            ? ErrorOr<TemplateMatch?>.ForValue(null)
            : await FindExactAsync(store, new TemplateLookup(request.Type, language, request.Scope, request.Part, request.Kind), ct);
        return IsMissing(scoped) && FallsBackToNoScope(request)
            ? await FindExactAsync(store, new TemplateLookup(request.Type, language, Scope: null, request.Part, request.Kind), ct)
            : scoped;
    }

    private static async ValueTask<ErrorOr<TemplateMatch?>> FindExactAsync(ITemplateStore store, TemplateLookup lookup, CancellationToken ct)
    {
        var source = await store.FindAsync(lookup, ct);
        if (source.IsError)
        {
            return source.Error;
        }

        return source.Value is { } found
            ? ErrorOr<TemplateMatch?>.ForValue(new TemplateMatch(found, lookup))
            : ErrorOr<TemplateMatch?>.ForValue(null);
    }

    private static bool FallsBackToNoScope(TemplateRequest request) => request.Scope is null || !request.Optional;

    private static bool IsMissing(ErrorOr<TemplateMatch?> match) => match.IsSuccess && match.Value is null;

    private bool IsDefaultLanguage(string language) => string.Equals(language, _defaultLanguage, StringComparison.OrdinalIgnoreCase);

    private string LanguagesOf(TemplateRequest request) =>
        request.Optional || IsDefaultLanguage(request.Language) ? request.Language : $"{request.Language} or {_defaultLanguage}";
}
