using MsgHatch.Domain.Failures;
using MsgHatch.Templates.Placeholders;

namespace MsgHatch.Templates.Placeholders;

/// <summary>
/// Parses the text of one template part (section 4.10). A placeholder is <c>{{PropertyName}}</c>. A <c>}}</c> outside a
/// placeholder is literal text. An unclosed <c>{{</c> and a name that the placeholder writer does not know (D5) give
/// <see cref="Errors.TemplateInvalid"/>. A generated writer knows only the property names, so a name with other
/// characters than letters, digits, and <c>_</c> is unknown.
/// </summary>
internal static class TemplateParser
{
    public static ErrorOr<ParsedTemplate> Parse<TPayload>(TemplateSource source, TemplatePartKind kind, IPlaceholderWriter<TPayload> writer)
    {
        var text = source.Text;
        var segments = new List<ParsedTemplate.Segment>();
        var position = 0;
        while (position < text.Length)
        {
            var open = text.IndexOf("{{", position, StringComparison.Ordinal);
            if (open < 0)
            {
                segments.Add(new ParsedTemplate.Segment(text[position..], -1));
                break;
            }

            if (open > position)
            {
                segments.Add(new ParsedTemplate.Segment(text[position..open], -1));
            }

            var close = text.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                return Invalid($"The template {source.Key.Id} has an unclosed placeholder at {open}.");
            }

            var index = writer.IndexOf(text.AsSpan(open + 2, close - open - 2));
            if (index < 0)
            {
                return Invalid($"The template {source.Key.Id} has an invalid or unknown placeholder at {open}.");
            }

            segments.Add(new ParsedTemplate.Segment(null, index));
            position = close + 2;
        }

        return new ParsedTemplate(kind, [.. segments]);
    }

    private static Error Invalid(string message) => new(Errors.TemplateInvalid, message);
}
