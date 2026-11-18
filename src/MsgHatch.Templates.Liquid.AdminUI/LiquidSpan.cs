namespace MsgHatch.Templates.Liquid.AdminUI;

/// <summary>One span of a Liquid text.</summary>
/// <param name="Kind">The kind of the span.</param>
/// <param name="Start">The index of the first character.</param>
/// <param name="Length">The number of characters.</param>
public readonly record struct LiquidSpan(LiquidSpanKind Kind, int Start, int Length);
