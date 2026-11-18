namespace MsgHatch.Templates.AdminUI;

/// <summary>The segments of the highlighted layer of a template text with no engine tokens (D252).</summary>
internal static class TextSegments
{
    /// <summary>Gives the HTML spans of an html part, and one segment with no class for a text part.</summary>
    public static List<TextSegment> Of(string text, TemplatePartKind kind) =>
        kind == TemplatePartKind.Html
            ? [.. HtmlTokenizer.Tokenize(text).Select(static span => new TextSegment(span.Start, span.Length, HtmlTokenizer.CssClassOf(span.Kind)))]
            : [new TextSegment(0, text.Length, string.Empty)];
}
