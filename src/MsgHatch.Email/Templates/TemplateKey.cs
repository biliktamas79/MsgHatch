namespace MsgHatch.Email.Templates;

/// <summary>The identity of one version of a template, the key of the parsed template cache (section 4.10).</summary>
/// <param name="Id">The id of the template in its store, for example the full path with no part suffix.</param>
/// <param name="Version">The version, for example the latest last write time of the parts in ticks, or 0 for a template that does not change.</param>
public readonly record struct TemplateKey(string Id, long Version);
