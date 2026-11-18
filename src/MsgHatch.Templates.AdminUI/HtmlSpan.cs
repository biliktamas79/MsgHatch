namespace MsgHatch.Templates.AdminUI;

/// <summary>One span of an HTML text.</summary>
/// <param name="Kind">The kind of the span.</param>
/// <param name="Start">The index of the first character.</param>
/// <param name="Length">The number of characters.</param>
public readonly record struct HtmlSpan(HtmlSpanKind Kind, int Start, int Length);
