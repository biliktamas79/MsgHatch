using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Placeholders;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Engines;

/// <summary>
/// The placeholder engine (D45, section 4.10). It selects a template with the fallback of D123, parses and binds it one
/// time for each <see cref="TemplateKey"/>, and replaces each <c>{{PropertyName}}</c> with a payload property. A
/// render completes synchronously. A lookup that hits the cache of the prepared templates allocates nothing (section 4.11).
/// </summary>
/// <typeparam name="TPayload">The payload type, with the placeholder writer of the source generator.</typeparam>
public sealed class PlaceholderEmailEngine<TPayload> : IEmailTemplateEngine<TPayload>
    where TPayload : IPlaceholderPayload<TPayload>
{
    private readonly TemplateSelector _selector;
    private readonly PlaceholderParts _parts;

    /// <summary>Makes the engine of one tenant key, with all parts.</summary>
    /// <param name="tenantStore">The template store of the tenant, or null for no tenant or no tenant template store.</param>
    /// <param name="sharedStore">The shared template store, or null when none is registered.</param>
    /// <param name="options">The options, with the default language of the fallback.</param>
    public PlaceholderEmailEngine(ITemplateStore? tenantStore, ITemplateStore? sharedStore, IOptions<MsgHatchOptions> options)
        : this(tenantStore, sharedStore, DefaultLanguageOf(options), PlaceholderParts.All)
    {
    }

    /// <summary>
    /// Makes the engine of one tenant key with the parts of its templates (D197). A default language that is the
    /// language of a request gives no fallback to another language.
    /// </summary>
    /// <param name="tenantStore">The template store of the tenant, or null for no tenant or no tenant template store.</param>
    /// <param name="sharedStore">The shared template store, or null when none is registered.</param>
    /// <param name="defaultLanguage">The default language of the fallback.</param>
    /// <param name="parts">The parts of the templates.</param>
    public PlaceholderEmailEngine(ITemplateStore? tenantStore, ITemplateStore? sharedStore, string defaultLanguage, PlaceholderParts parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLanguage);
        if (!Enum.IsDefined(parts))
        {
            throw new ArgumentOutOfRangeException(nameof(parts), parts, "The parts are All or SubjectAndText.");
        }

        _selector = new TemplateSelector(tenantStore, sharedStore, defaultLanguage);
        _parts = parts;
    }

    /// <inheritdoc/>
    public async ValueTask<ErrorOr<IEmailTemplate<TPayload>>> PrepareAsync(EmailTemplateRequest request, CancellationToken ct)
    {
        var source = await _selector.FindAsync(request, ct);
        if (source.IsError)
        {
            return source.Error;
        }

        var template = ParsedTemplateCache<TPayload>.GetOrParse(source.Value, _parts);
        return template.IsError ? template.Error : ErrorOr<IEmailTemplate<TPayload>>.ForValue(template.Value);
    }

    private static string DefaultLanguageOf(IOptions<MsgHatchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Value.DefaultLanguage;
    }
}
