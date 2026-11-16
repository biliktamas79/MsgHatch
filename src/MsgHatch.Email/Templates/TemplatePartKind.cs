namespace MsgHatch.Email.Templates;

/// <summary>
/// The kind of a template part (D214). It gives the file extension of the part, and an engine encodes each value in an
/// <see cref="Html"/> part.
/// </summary>
public enum TemplatePartKind
{
    /// <summary>Text with no encoding, in a file <c>.txt</c>, for example the subject.</summary>
    PlainText,

    /// <summary>HTML, in a file <c>.html</c>.</summary>
    Html,
}
