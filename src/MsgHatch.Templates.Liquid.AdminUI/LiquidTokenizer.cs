using System.Text.RegularExpressions;

namespace MsgHatch.Templates.Liquid.AdminUI;

/// <summary>
/// Splits a Liquid text into the spans that the Liquid editor highlights, and finds the problems that need no call of the
/// server (D242): an open <c>{{</c> or <c>{%</c>, and a <c>{% liquid %}</c> tag (D226). It is no parser: the check of the
/// server stays the only full check, with the filters of the host (D241).
/// </summary>
public static partial class LiquidTokenizer
{
    /// <summary>The message of a <c>{{</c> with no <c>}}</c>.</summary>
    public const string OpenOutputMessage = "The {{ has no }}.";

    /// <summary>The message of a <c>{%</c> with no <c>%}</c>.</summary>
    public const string OpenTagMessage = "The {% has no %}.";

    /// <summary>The message of a <c>{% liquid %}</c> tag, which the Liquid engine does not allow (D226).</summary>
    public const string LiquidTagMessage = "The engine does not allow the {% liquid %} tag.";

    /// <summary>Splits a text into spans. Each character is in exactly one span, and the spans are in the order of the text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The spans.</returns>
    public static IReadOnlyList<LiquidSpan> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Scan(text).Spans;
    }

    /// <summary>Finds an open <c>{{</c> or <c>{%</c>, and each <c>{% liquid %}</c> tag, with the line and the column of each one.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The issues, in the order of the text.</returns>
    public static IReadOnlyList<TemplateIssue> FindIssues(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var issues = new List<(int Offset, string Message)>();
        foreach (Match tag in LiquidTag().Matches(text))
        {
            issues.Add((tag.Index, LiquidTagMessage));
        }

        if (Scan(text).OpenAt is { } open)
        {
            issues.Add((open, text[open + 1] == '{' ? OpenOutputMessage : OpenTagMessage));
        }

        return [.. issues.OrderBy(static issue => issue.Offset).Select(issue => IssueAt(text, issue.Offset, issue.Message))];
    }

    internal static int? OffsetOf(string text, int line, int column)
    {
        var start = 0;
        for (var current = 1; current < line; current++)
        {
            var next = text.IndexOf('\n', start);
            if (next < 0)
            {
                return null;
            }

            start = next + 1;
        }

        var offset = start + column - 1;
        return column >= 1 && offset < text.Length ? offset : null;
    }

    private static TemplateIssue IssueAt(string text, int offset, string message)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(offset - 1, 0)) + 1;
        var line = 1 + text.AsSpan(0, lineStart).Count('\n');
        return new TemplateIssue(message, line, offset - lineStart + 1);
    }

    private static (List<LiquidSpan> Spans, int? OpenAt) Scan(string text)
    {
        var spans = new List<LiquidSpan>();
        var index = 0;
        int? openAt = null;
        while (index < text.Length)
        {
            var open = NextOpen(text, index);
            if (open < 0)
            {
                Add(spans, LiquidSpanKind.Text, index, text.Length - index);
                break;
            }

            Add(spans, LiquidSpanKind.Text, index, open - index);
            (index, var closed) = ReadTag(text, open, spans);
            openAt = closed ? openAt : open;
        }

        return (spans, openAt);
    }

    private static int NextOpen(string text, int from)
    {
        for (var index = text.IndexOf('{', from); index >= 0 && index < text.Length - 1; index = text.IndexOf('{', index + 1))
        {
            if (text[index + 1] is '{' or '%')
            {
                return index;
            }
        }

        return -1;
    }

    private static (int Next, bool Closed) ReadTag(string text, int open, List<LiquidSpan> spans)
    {
        var isOutput = text[open + 1] == '{';
        var openLength = open + 2 < text.Length && text[open + 2] == '-' ? 3 : 2;
        Add(spans, LiquidSpanKind.Delimiter, open, openLength);
        var index = open + openLength;
        var tagName = isOutput ? null : ReadTagName(text, ref index, spans);
        var afterPipe = false;
        while (index < text.Length)
        {
            if (CloseLengthAt(text, index, isOutput) is var close and > 0)
            {
                Add(spans, LiquidSpanKind.Delimiter, index, close);
                return (ReadBlockOf(tagName, text, index + close, spans), true);
            }

            index = ReadCode(text, index, ref afterPipe, spans);
        }

        return (index, false);
    }

    private static string? ReadTagName(string text, ref int index, List<LiquidSpan> spans)
    {
        var start = index;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        Add(spans, LiquidSpanKind.Code, start, index - start);
        var end = IdentifierEnd(text, index);
        if (end == index)
        {
            return null;
        }

        Add(spans, LiquidSpanKind.TagName, index, end - index);
        var name = text[index..end];
        index = end;
        return name;
    }

    private static int ReadCode(string text, int index, ref bool afterPipe, List<LiquidSpan> spans)
    {
        var character = text[index];
        if (character is '\'' or '"')
        {
            var end = text.IndexOf(character, index + 1);
            end = end < 0 ? text.Length : end + 1;
            Add(spans, LiquidSpanKind.Quoted, index, end - index);
            afterPipe = false;
            return end;
        }

        if (char.IsAsciiDigit(character))
        {
            var end = NumberEnd(text, index);
            Add(spans, LiquidSpanKind.Number, index, end - index);
            afterPipe = false;
            return end;
        }

        if (char.IsAsciiLetter(character) || character == '_')
        {
            var end = IdentifierEnd(text, index);
            Add(spans, afterPipe ? LiquidSpanKind.FilterName : LiquidSpanKind.Code, index, end - index);
            afterPipe = false;
            return end;
        }

        afterPipe = character == '|' || (afterPipe && char.IsWhiteSpace(character));
        Add(spans, LiquidSpanKind.Code, index, 1);
        return index + 1;
    }

    private static int ReadBlockOf(string? tagName, string text, int index, List<LiquidSpan> spans)
    {
        var (kind, endTag) = tagName switch
        {
            "comment" => (LiquidSpanKind.Comment, EndCommentTag()),
            "raw" => (LiquidSpanKind.Raw, EndRawTag()),
            _ => (LiquidSpanKind.Text, null),
        };
        if (endTag is null)
        {
            return index;
        }

        var match = endTag.Match(text, index);
        var end = match.Success ? match.Index : text.Length;
        Add(spans, kind, index, end - index);
        return end;
    }

    private static int CloseLengthAt(string text, int index, bool isOutput)
    {
        var close = isOutput ? "}}" : "%}";
        return string.CompareOrdinal(text, index, "-" + close, 0, 3) == 0 ? 3
            : string.CompareOrdinal(text, index, close, 0, 2) == 0 ? 2
            : 0;
    }

    private static int IdentifierEnd(string text, int index)
    {
        if (index >= text.Length || !(char.IsAsciiLetter(text[index]) || text[index] == '_'))
        {
            return index;
        }

        while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] == '_'))
        {
            index++;
        }

        return index;
    }

    private static int NumberEnd(string text, int index)
    {
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        if (index + 1 < text.Length && text[index] == '.' && char.IsAsciiDigit(text[index + 1]))
        {
            index++;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
            }
        }

        return index;
    }

    private static void Add(List<LiquidSpan> spans, LiquidSpanKind kind, int start, int length)
    {
        if (length <= 0)
        {
            return;
        }

        if (spans.Count > 0 && spans[^1] is { } last && last.Kind == kind && kind is LiquidSpanKind.Code or LiquidSpanKind.Text && last.Start + last.Length == start)
        {
            spans[^1] = last with { Length = last.Length + length };
            return;
        }

        spans.Add(new LiquidSpan(kind, start, length));
    }

    [GeneratedRegex(@"\{%-?\s*liquid\b")]
    private static partial Regex LiquidTag();

    [GeneratedRegex(@"\{%-?\s*endcomment\s*-?%\}")]
    private static partial Regex EndCommentTag();

    [GeneratedRegex(@"\{%-?\s*endraw\s*-?%\}")]
    private static partial Regex EndRawTag();
}
