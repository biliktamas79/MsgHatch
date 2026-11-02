namespace MsgHatch.Email.Templates;

/// <summary>The kind of a template part. The placeholder engine encodes each value in an <see cref="Html"/> part.</summary>
public enum TemplatePartKind
{
    /// <summary>Text with no encoding, for example the subject.</summary>
    PlainText,

    /// <summary>HTML.</summary>
    Html,
}
