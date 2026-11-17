namespace MsgHatch.Templates;

/// <summary>One template that <see cref="TemplateSelector"/> found, with the lookup that found it (D123, D214).</summary>
/// <param name="Source">The text and the key of the template.</param>
/// <param name="Lookup">The lookup that found it: the language and the template scope after the fallback.</param>
public readonly record struct TemplateMatch(TemplateSource Source, TemplateLookup Lookup);
