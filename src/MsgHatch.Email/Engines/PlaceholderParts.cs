namespace MsgHatch.Email.Engines;

/// <summary>The parts of the templates of a placeholder engine (section 4.10, D197).</summary>
public enum PlaceholderParts
{
    /// <summary>The required parts <c>subject</c> and <c>html</c>, and the optional part <c>text</c>.</summary>
    All,

    /// <summary>
    /// The required part <c>subject</c> and the optional part <c>text</c>, for an engine that renders the html itself,
    /// for example the Razor engine. An <c>html</c> part gives <see cref="Domain.Failures.Errors.TemplateInvalid"/>.
    /// </summary>
    SubjectAndText,
}
