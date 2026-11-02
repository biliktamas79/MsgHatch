namespace MsgHatch.Email.Templates;

/// <summary>A template with each part split into literal and placeholder segments, and each placeholder bound to a property index (section 4.10).</summary>
internal sealed record ParsedTemplate(ParsedTemplate.Part Subject, ParsedTemplate.Part Html, ParsedTemplate.Part? Text)
{
    /// <summary>One parsed part.</summary>
    public sealed record Part(TemplatePartKind Kind, Segment[] Segments);

    /// <summary>A literal text, or the property index of a placeholder when <see cref="Literal"/> is null.</summary>
    public readonly record struct Segment(string? Literal, int PropertyIndex);
}
