using MsgHatch.Domain.Identity;

namespace MsgHatch.Email.Templates;

/// <summary>One lookup of a template store (section 4.10).</summary>
/// <param name="Type">The message type.</param>
/// <param name="Language">The culture name of the language.</param>
/// <param name="Scope">The template scope, or null for the template with no scope.</param>
public readonly record struct TemplateLookup(MessageType Type, string Language, string? Scope);
