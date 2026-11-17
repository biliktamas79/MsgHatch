namespace MsgHatch.Templates;

/// <summary>A part of a template that a channel reads, which the channel declares when it registers its engine (D220).</summary>
/// <param name="Name">The name of the part, ASCII letters, digits, and <c>_</c>, which starts with a letter, for example <c>subject</c>.</param>
/// <param name="Kind">The kind of the part.</param>
public readonly record struct TemplatePart(string Name, TemplatePartKind Kind);
