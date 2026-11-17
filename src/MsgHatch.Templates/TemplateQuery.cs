using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates;

/// <summary>The filters of a list of templates (D218). A null filter matches each value.</summary>
/// <param name="Type">The message type.</param>
/// <param name="Scope">The template scope. Null matches each scope, and also the templates with no scope.</param>
public sealed record TemplateQuery(MessageType? Type = null, string? Scope = null);
