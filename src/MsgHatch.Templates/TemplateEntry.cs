namespace MsgHatch.Templates;

/// <summary>One stored template of a writable template store, with no text (D218).</summary>
/// <param name="Lookup">The message type, the language, the template scope, the part, and its kind.</param>
/// <param name="Version">The version, the same as the version of <see cref="TemplateKey"/> of its source.</param>
public sealed record TemplateEntry(TemplateLookup Lookup, long Version);
