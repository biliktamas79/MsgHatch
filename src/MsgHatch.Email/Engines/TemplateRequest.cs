using MsgHatch.Domain.Identity;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Engines;

/// <summary>The request for one part of the template of a message type, language, and template scope (D213, D214).</summary>
/// <param name="Type">The message type.</param>
/// <param name="Language">The culture name of the language.</param>
/// <param name="Scope">The template scope, or null for the templates with no scope.</param>
/// <param name="Part">The name of the part, for example <c>subject</c> or <c>body</c>.</param>
/// <param name="Kind">The kind of the part. An <see cref="TemplatePartKind.Html"/> part encodes each value.</param>
public readonly record struct TemplateRequest(MessageType Type, string Language, string? Scope, string Part, TemplatePartKind Kind);
