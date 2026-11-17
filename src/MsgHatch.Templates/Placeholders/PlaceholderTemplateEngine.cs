using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Placeholders;

/// <summary>
/// The placeholder engine (D45, section 4.10). It selects the template of one part with the fallback of D123, parses
/// and binds it one time for each <see cref="TemplateKey"/>, and replaces each <c>{{PropertyName}}</c> with a payload
/// property. A render completes synchronously. A lookup that hits the cache of the prepared templates allocates nothing
/// (section 4.11).
/// </summary>
/// <typeparam name="TPayload">The payload type, with the placeholder writer of the source generator.</typeparam>
public sealed class PlaceholderTemplateEngine<TPayload> : ITemplateEngine<TPayload>
    where TPayload : IPlaceholderPayload<TPayload>
{
    private readonly TemplateSelector _selector;

    /// <summary>Makes the engine of one tenant key.</summary>
    /// <param name="tenantStore">The template store of the tenant, or null for no tenant or no tenant template store.</param>
    /// <param name="sharedStore">The shared template store, or null when none is registered.</param>
    /// <param name="options">The options, with the default language of the fallback.</param>
    public PlaceholderTemplateEngine(ITemplateStore? tenantStore, ITemplateStore? sharedStore, IOptions<MsgHatchOptions> options)
        : this(new TemplateSelector(tenantStore, sharedStore, (options ?? throw new ArgumentNullException(nameof(options))).Value.DefaultLanguage))
    {
    }

    /// <summary>Makes the engine of one tenant key with a template selector.</summary>
    /// <param name="selector">The template selector of the tenant key.</param>
    public PlaceholderTemplateEngine(TemplateSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = selector;
    }

    /// <inheritdoc/>
    public async ValueTask<ErrorOr<ITemplate<TPayload>>> PrepareAsync(TemplateRequest request, CancellationToken ct)
    {
        var source = await _selector.FindAsync(request, ct);
        if (source.IsError)
        {
            return source.Error;
        }

        var template = ParsedTemplateCache<TPayload>.GetOrParse(source.Value);
        return template.IsError ? template.Error : ErrorOr<ITemplate<TPayload>>.ForValue(template.Value);
    }

    /// <inheritdoc/>
    public ValueTask<Error?> CheckAsync(TemplateLookup lookup, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        var source = new TemplateSource(new TemplateKey($"{lookup.Type.Name}.{lookup.Language}.{lookup.Part}", 0), text);
        return ValueTask.FromResult(TemplateParser.Parse(source, lookup.Kind, TPayload.Writer).Error);
    }
}
