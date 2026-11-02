namespace MsgHatch.Email.Templates;

/// <summary>One template of a template store, with each of its parts.</summary>
/// <param name="Key">The identity of this version of the template.</param>
/// <param name="Parts">The parts of the template.</param>
public sealed record TemplateSource(TemplateKey Key, IReadOnlyList<TemplatePart> Parts);
