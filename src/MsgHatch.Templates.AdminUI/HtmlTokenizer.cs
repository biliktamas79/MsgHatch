namespace MsgHatch.Templates.AdminUI;

/// <summary>
/// Splits an HTML text into the spans that an editor highlights (D252): the tags with their names, attributes, and values,
/// the comments, and the character references. It is no parser, so a text with an error gives spans too.
/// </summary>
public static class HtmlTokenizer
{
    private const int _maxEntityLength = 32;

    /// <summary>
    /// Splits a text into spans. Each character is in exactly one span, and the spans are in the order of the text. A tag, a
    /// comment, or a quoted value with no end runs to the end of the text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The spans.</returns>
    public static IReadOnlyList<HtmlSpan> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var scanner = new Scanner(text);
        scanner.Run();
        return scanner.Spans;
    }

    /// <summary>Gives the CSS class of the highlight of a span kind, for example <c>html-tagname</c>.</summary>
    /// <param name="kind">The kind of the span.</param>
    /// <returns>The CSS class.</returns>
    public static string CssClassOf(HtmlSpanKind kind) => kind switch
    {
        HtmlSpanKind.Markup => "html-markup",
        HtmlSpanKind.TagName => "html-tagname",
        HtmlSpanKind.AttributeName => "html-attribute",
        HtmlSpanKind.AttributeValue => "html-value",
        HtmlSpanKind.Comment => "html-comment",
        HtmlSpanKind.Entity => "html-entity",
        _ => "html-text",
    };

    private sealed class Scanner(string text)
    {
        private int _index;
        private int _textStart;

        public List<HtmlSpan> Spans { get; } = [];

        public void Run()
        {
            while (_index < text.Length)
            {
                if (IsAt("<!--"))
                {
                    AddText();
                    ReadComment();
                }
                else if (IsTagStart())
                {
                    AddText();
                    ReadTag();
                }
                else if (EntityLength() is > 0 and var length)
                {
                    AddText();
                    Add(HtmlSpanKind.Entity, length);
                }
                else
                {
                    _index++;
                    continue;
                }

                _textStart = _index;
            }

            AddText();
        }

        private void ReadComment()
        {
            var end = text.IndexOf("-->", _index + 4, StringComparison.Ordinal);
            Add(HtmlSpanKind.Comment, (end < 0 ? text.Length : end + 3) - _index);
        }

        private void ReadTag()
        {
            Add(HtmlSpanKind.Markup, text[_index + 1] is '/' or '!' ? 2 : 1);
            AddWhile(HtmlSpanKind.TagName, IsNameChar);
            while (_index < text.Length)
            {
                if (text[_index] == '>')
                {
                    Add(HtmlSpanKind.Markup, 1);
                    return;
                }

                if (IsAt("/>"))
                {
                    Add(HtmlSpanKind.Markup, 2);
                    return;
                }

                ReadInsideTag();
            }
        }

        private void ReadInsideTag()
        {
            if (char.IsWhiteSpace(text[_index]))
            {
                AddWhile(HtmlSpanKind.Text, char.IsWhiteSpace);
            }
            else if (text[_index] == '=')
            {
                Add(HtmlSpanKind.Markup, 1);
                AddWhile(HtmlSpanKind.Text, char.IsWhiteSpace);
                ReadValue();
            }
            else
            {
                AddWhile(HtmlSpanKind.AttributeName, IsAttributeNameChar);
            }
        }

        private void ReadValue()
        {
            if (_index >= text.Length)
            {
                return;
            }

            if (text[_index] is '"' or '\'')
            {
                var end = text.IndexOf(text[_index], _index + 1);
                Add(HtmlSpanKind.AttributeValue, (end < 0 ? text.Length : end + 1) - _index);
                return;
            }

            AddWhile(HtmlSpanKind.AttributeValue, static character => !char.IsWhiteSpace(character) && character != '>');
        }

        private int EntityLength()
        {
            if (text[_index] != '&')
            {
                return 0;
            }

            var end = _index + 1;
            Func<char, bool> isBodyChar = char.IsAsciiLetterOrDigit;
            if (end < text.Length && text[end] == '#')
            {
                var hex = end + 1 < text.Length && text[end + 1] is 'x' or 'X';
                isBodyChar = hex ? char.IsAsciiHexDigit : char.IsAsciiDigit;
                end += hex ? 2 : 1;
            }

            var bodyStart = end;
            while (end < text.Length && end - _index < _maxEntityLength && isBodyChar(text[end]))
            {
                end++;
            }

            return end > bodyStart && end < text.Length && text[end] == ';' ? end + 1 - _index : 0;
        }

        private bool IsTagStart() =>
            text[_index] == '<' && _index + 1 < text.Length && (char.IsAsciiLetter(text[_index + 1]) || text[_index + 1] is '/' or '!');

        private bool IsAttributeNameChar(char character) =>
            !char.IsWhiteSpace(character) && character is not '=' and not '>' && !IsAt("/>");

        private static bool IsNameChar(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or ':' or '_' or '.';

        private bool IsAt(string value) => text.AsSpan(_index).StartsWith(value, StringComparison.Ordinal);

        private void AddText()
        {
            if (_index > _textStart)
            {
                Spans.Add(new HtmlSpan(HtmlSpanKind.Text, _textStart, _index - _textStart));
            }
        }

        private void Add(HtmlSpanKind kind, int length)
        {
            Spans.Add(new HtmlSpan(kind, _index, length));
            _index += length;
        }

        private void AddWhile(HtmlSpanKind kind, Func<char, bool> isPart)
        {
            var start = _index;
            while (_index < text.Length && isPart(text[_index]))
            {
                _index++;
            }

            if (_index > start)
            {
                Spans.Add(new HtmlSpan(kind, start, _index - start));
            }
        }
    }
}
