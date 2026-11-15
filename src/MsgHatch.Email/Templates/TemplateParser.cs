using MsgHatch.Domain.Failures;
using MsgHatch.Email.Placeholders;

namespace MsgHatch.Email.Templates;

/// <summary>
/// Parses the parts of a template (section 4.10). A placeholder is <c>{{PropertyName}}</c>. A <c>}}</c> outside a
/// placeholder is literal text. An unclosed <c>{{</c>, a name that the placeholder writer does not know (D5), and a
/// missing <c>subject</c> or <c>html</c> part give <see cref="Errors.TemplateInvalid"/>. A generated writer knows only
/// the property names, so a name with other characters than letters, digits, and <c>_</c> is unknown.
/// </summary>
internal static class TemplateParser
{
    public static ErrorOr<ParsedTemplate> Parse<TPayload>(TemplateSource source, IPlaceholderWriter<TPayload> writer)
    {
        var subject = ParseRequired(source, "subject", writer);
        var html = ParseRequired(source, "html", writer);
        var text = ParseOptional(source, "text", writer);
        if (subject.IsError)
        {
            return subject.Error;
        }

        if (html.IsError)
        {
            return html.Error;
        }

        return text.IsError ? text.Error : new ParsedTemplate(subject.Value, html.Value, text.Value);
    }

    private static ErrorOr<ParsedTemplate.Part> ParseRequired<TPayload>(TemplateSource source, string name, IPlaceholderWriter<TPayload> writer) =>
        source.Parts.FirstOrDefault(part => part.Name == name) is { } part
            ? ParsePart(part, writer)
            : Invalid($"The template {source.Key.Id} has no {name} part.");

    private static ErrorOr<ParsedTemplate.Part?> ParseOptional<TPayload>(TemplateSource source, string name, IPlaceholderWriter<TPayload> writer) =>
        source.Parts.FirstOrDefault(part => part.Name == name) is { } part
            ? ParsePart(part, writer).ThenIfSuccess(static parsed => ErrorOr<ParsedTemplate.Part?>.ForValue(parsed))
            : ErrorOr<ParsedTemplate.Part?>.ForValue(null);

    private static ErrorOr<ParsedTemplate.Part> ParsePart<TPayload>(TemplatePart part, IPlaceholderWriter<TPayload> writer)
    {
        var text = part.Text;
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
                return Invalid($"The {part.Name} part has an unclosed placeholder at {open}.");
            }

            var index = writer.IndexOf(text.AsSpan(open + 2, close - open - 2));
            if (index < 0)
            {
                return Invalid($"The {part.Name} part has an invalid or unknown placeholder at {open}.");
            }

            segments.Add(new ParsedTemplate.Segment(null, index));
            position = close + 2;
        }

        return new ParsedTemplate.Part(part.Kind, [.. segments]);
    }

    private static Error Invalid(string message) => new(Errors.TemplateInvalid, message);
}
