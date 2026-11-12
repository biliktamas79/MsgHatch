using System.Globalization;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.Placeholders;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Engines;

/// <summary>
/// One prepared template of the placeholder engine (section 4.10). The cache of the prepared templates keeps one
/// instance for each template version, and gives it to each lookup.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
/// <param name="parsed">The parsed and bound template.</param>
internal sealed class PlaceholderEmailTemplate<TPayload>(ParsedTemplate parsed) : IEmailTemplate<TPayload>
    where TPayload : IPlaceholderPayload<TPayload>
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
