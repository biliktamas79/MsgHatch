namespace MsgHatch.Templates.Liquid.AdminUI;

internal readonly record struct LiquidSegment(LiquidSpanKind Kind, int Start, int Length, string? Mark)
{
    public string CssClass => Kind switch
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
}

internal static class LiquidSegments
{
    public static List<LiquidSegment> Of(string text, Dictionary<int, string> marks)
    {
        var segments = new List<LiquidSegment>();
        foreach (var span in LiquidTokenizer.Tokenize(text))
        {
            var start = span.Start;
            var end = span.Start + span.Length;
            for (var offset = start; offset < end; offset++)
            {
                if (marks.TryGetValue(offset, out var mark))
                {
                    AddUnmarked(segments, span.Kind, start, offset);
                    segments.Add(new LiquidSegment(span.Kind, offset, 1, mark));
                    start = offset + 1;
                }
            }

            AddUnmarked(segments, span.Kind, start, end);
        }

        return segments;
    }

    private static void AddUnmarked(List<LiquidSegment> segments, LiquidSpanKind kind, int start, int end)
    {
        if (end > start)
        {
            segments.Add(new LiquidSegment(kind, start, end - start, Mark: null));
        }
    }
}
