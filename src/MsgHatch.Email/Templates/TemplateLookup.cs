using MsgHatch.Domain.Identity;

namespace MsgHatch.Email.Templates;

/// <summary>One lookup of a template store: one part of a template (section 4.10, D214).</summary>
/// <param name="Type">The message type.</param>
/// <param name="Language">The culture name of the language.</param>
/// <param name="Scope">The template scope, or null for the template with no scope.</param>
/// <param name="Part">The name of the part, for example <c>subject</c> or <c>body</c>.</param>
/// <param name="Kind">The kind of the part, which gives the file extension <c>.txt</c> or <c>.html</c>.</param>
public readonly record struct TemplateLookup(MessageType Type, string Language, string? Scope, string Part, TemplatePartKind Kind);
