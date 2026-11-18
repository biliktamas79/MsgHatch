using MsgHatch.Templates.AdminUI;

namespace MsgHatch.Templates.Liquid.AdminUI;

internal static class LiquidSegments
{
    private const char _codeMask = 'x';

    public static List<TextSegment> Of(string text, Dictionary<int, string> marks, bool html)
    {
        var htmlSpans = html ? HtmlTokenizer.Tokenize(MaskLiquidCode(text)) : [];
        var segments = new List<TextSegment>();
        foreach (var span in LiquidTokenizer.Tokenize(text))
        {
            if (html && span.Kind == LiquidSpanKind.Text)
            {
                foreach (var piece in HtmlPiecesOf(htmlSpans, span.Start, span.Start + span.Length))
                {
                    AddWithMarks(segments, piece, marks);
                }
            }
            else
            {
                AddWithMarks(segments, new TextSegment(span.Start, span.Length, CssClassOf(span.Kind)), marks);
            }
        }

        return segments;
    }

    private static string CssClassOf(LiquidSpanKind kind) => kind switch
    {
        LiquidSpanKind.Delimiter => "liquid-delimiter",
        LiquidSpanKind.TagName => "liquid-tagname",
        LiquidSpanKind.FilterName => "liquid-filtername",
        LiquidSpanKind.Quoted => "liquid-quoted",
        LiquidSpanKind.Number => "liquid-number",
        LiquidSpanKind.Code => "liquid-code",
        LiquidSpanKind.Comment => "liquid-comment",
        LiquidSpanKind.Raw => "liquid-raw",
        _ => "liquid-text",
    };

    private static string MaskLiquidCode(string text)
    {
        var masked = text.ToCharArray();
        foreach (var span in LiquidTokenizer.Tokenize(text))
        {
            if (span.Kind != LiquidSpanKind.Text)
            {
                masked.AsSpan(span.Start, span.Length).Fill(_codeMask);
            }
        }

        return new string(masked);
    }

    private static IEnumerable<TextSegment> HtmlPiecesOf(IReadOnlyList<HtmlSpan> htmlSpans, int start, int end)
    {
        foreach (var span in htmlSpans)
        {
            var pieceStart = Math.Max(start, span.Start);
            var pieceEnd = Math.Min(end, span.Start + span.Length);
            if (pieceEnd > pieceStart)
            {
                yield return new TextSegment(pieceStart, pieceEnd - pieceStart, HtmlTokenizer.CssClassOf(span.Kind));
            }
        }
    }

    private static void AddWithMarks(List<TextSegment> segments, TextSegment piece, Dictionary<int, string> marks)
    {
        var start = piece.Start;
        var end = piece.Start + piece.Length;
        for (var offset = start; offset < end; offset++)
        {
            if (marks.TryGetValue(offset, out var mark))
            {
                AddUnmarked(segments, piece.CssClass, start, offset);
                segments.Add(new TextSegment(offset, 1, piece.CssClass, mark));
                start = offset + 1;
            }
        }

        AddUnmarked(segments, piece.CssClass, start, end);
    }

    private static void AddUnmarked(List<TextSegment> segments, string cssClass, int start, int end)
    {
        if (end > start)
        {
            segments.Add(new TextSegment(start, end - start, cssClass));
        }
    }
}
