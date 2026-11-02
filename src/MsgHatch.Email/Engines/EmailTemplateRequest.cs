using MsgHatch.Domain.Identity;

namespace MsgHatch.Email.Engines;

/// <summary>The request for the template of one message type, language, and template scope (section 4.10).</summary>
/// <param name="Type">The message type.</param>
/// <param name="Language">The culture name of the language.</param>
/// <param name="Scope">The template scope, or null for the templates with no scope.</param>
public readonly record struct EmailTemplateRequest(MessageType Type, string Language, string? Scope);
