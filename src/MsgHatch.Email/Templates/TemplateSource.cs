namespace MsgHatch.Email.Templates;

/// <summary>The text of one template part in a template store (D214).</summary>
/// <param name="Key">The identity of this version of the text.</param>
/// <param name="Text">The text, with its placeholders or its Liquid markup.</param>
public sealed record TemplateSource(TemplateKey Key, string Text);
