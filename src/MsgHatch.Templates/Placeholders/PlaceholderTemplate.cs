using System.Globalization;
using MsgHatch.Domain.Failures;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Placeholders;

/// <summary>
/// One prepared template of the placeholder engine: one part (section 4.10). The cache of the prepared templates keeps
/// one instance for each template version, and gives it to each lookup. A render completes synchronously and
/// allocates only its output string (section 4.11).
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
/// <param name="parsed">The parsed and bound part.</param>
/// <param name="lookup">The lookup that found the text. A store gives one key for each lookup, so the cache keeps it.</param>
internal sealed class PlaceholderTemplate<TPayload>(ParsedTemplate parsed, TemplateLookup lookup) : ITemplate<TPayload>
    where TPayload : IPlaceholderPayload<TPayload>
{
    public TemplateLookup Lookup => lookup;

    public ValueTask<ErrorOr<string>> RenderAsync(TPayload payload, CultureInfo culture, CancellationToken ct)
    {
        using var output = new PooledCharBufferWriter();
        using var value = new PooledCharBufferWriter();
        var rendered = PlaceholderRenderer.Render(parsed, payload, TPayload.Writer, culture, output, value);
        return ValueTask.FromResult(ErrorOr<string>.ForValue(rendered));
    }
}
