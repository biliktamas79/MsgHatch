namespace MsgHatch.Templates.AdminUI;

/// <summary>One segment of the highlighted layer of <see cref="HighlightedTextArea"/> (D252).</summary>
/// <param name="Start">The index of the first character in the text.</param>
/// <param name="Length">The number of characters.</param>
/// <param name="CssClass">The CSS class of the highlight, for example <c>html-tagname</c>.</param>
/// <param name="Mark">The mark of an issue at this segment, for example <c>3:9</c>, or null for no issue.</param>
public readonly record struct TextSegment(int Start, int Length, string CssClass, string? Mark = null);
