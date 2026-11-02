using System.Globalization;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.Placeholders;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Engines;

/// <summary>
/// The placeholder engine (D45, section 4.10). It selects a template with the fallback of D123, parses and binds it one
/// time for each <see cref="TemplateKey"/>, and replaces each <c>{{PropertyName}}</c> with a payload property. A
/// render completes synchronously.
/// </summary>
/// <typeparam name="TPayload">The payload type, with the placeholder writer of the source generator.</typeparam>
public sealed class PlaceholderEmailEngine<TPayload> : IEmailTemplateEngine<TPayload>
    where TPayload : IPlaceholderPayload<TPayload>
{
    private readonly TemplateSelector _selector;

    /// <summary>Makes the engine of one tenant key.</summary>
    /// <param name="tenantStore">The template store of the tenant, or null for no tenant or no tenant template store.</param>
    /// <param name="sharedStore">The shared template store, or null when none is registered.</param>
    /// <param name="options">The options, with the default language of the fallback.</param>
    public PlaceholderEmailEngine(ITemplateStore? tenantStore, ITemplateStore? sharedStore, IOptions<MsgHatchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _selector = new TemplateSelector(tenantStore, sharedStore, options.Value.DefaultLanguage);
    }

    /// <inheritdoc/>
    public async ValueTask<ErrorOr<IEmailTemplate<TPayload>>> PrepareAsync(EmailTemplateRequest request, CancellationToken ct)
    {
        var source = await _selector.FindAsync(request, ct);
        if (source.IsError)
        {
            return source.Error;
        }

        var parsed = ParsedTemplateCache<TPayload>.GetOrParse(source.Value);
        return parsed.IsError ? parsed.Error : ErrorOr<IEmailTemplate<TPayload>>.ForValue(new Template(parsed.Value));
    }

    private sealed class Template(ParsedTemplate parsed) : IEmailTemplate<TPayload>
    {
        public ValueTask<ErrorOr<RenderedEmail>> RenderAsync(TPayload payload, MessageRoute route, CultureInfo culture, CancellationToken ct)
        {
            using var output = new PooledCharBufferWriter();
            using var value = new PooledCharBufferWriter();
            var writer = TPayload.Writer;
            var rendered = new RenderedEmail(
                PlaceholderRenderer.Render(parsed.Subject, payload, writer, culture, output, value),
                PlaceholderRenderer.Render(parsed.Html, payload, writer, culture, output, value),
                parsed.Text is null ? null : PlaceholderRenderer.Render(parsed.Text, payload, writer, culture, output, value));
            return ValueTask.FromResult(ErrorOr<RenderedEmail>.ForValue(rendered));
        }
    }
}
