namespace MsgHatch.Email.Templates;

/// <summary>
/// One part of a template: <c>subject</c> (<see cref="TemplatePartKind.PlainText"/>, required), <c>html</c>
/// (<see cref="TemplatePartKind.Html"/>, required), or <c>text</c> (<see cref="TemplatePartKind.PlainText"/>, optional).
/// </summary>
/// <param name="Name">The name of the part.</param>
/// <param name="Kind">The kind of the part.</param>
/// <param name="Text">The text of the part, with its placeholders.</param>
public sealed record TemplatePart(string Name, TemplatePartKind Kind, string Text);
