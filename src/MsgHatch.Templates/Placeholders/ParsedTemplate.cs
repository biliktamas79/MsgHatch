namespace MsgHatch.Templates.Placeholders;

/// <summary>The text of one part split into literal and placeholder segments, and each placeholder bound to a property index (section 4.10).</summary>
/// <param name="Kind">The kind of the part.</param>
/// <param name="Segments">The segments.</param>
internal sealed record ParsedTemplate(TemplatePartKind Kind, ParsedTemplate.Segment[] Segments)
{
    /// <summary>A literal text, or the property index of a placeholder when <see cref="Literal"/> is null.</summary>
    public readonly record struct Segment(string? Literal, int PropertyIndex);
}
