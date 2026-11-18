namespace MsgHatch.Templates.AspNetCore;

/// <summary>The body of a save: the text, and the version that the caller read, or 0 for a new template (D223).</summary>
internal sealed record TemplateSaveRequest(string Text, long Version);

/// <summary>One template with no text, with no template id, because the id of the file store is a path on the host (D224).</summary>
internal sealed record TemplateEntryResponse(string MessageType, string Language, string? CompanyId, string Part, TemplatePartKind Kind, long Version)
{
    public static TemplateEntryResponse From(TemplateEntry entry) =>
        new(entry.Lookup.Type.Name, entry.Lookup.Language, entry.Lookup.Scope, entry.Lookup.Part, entry.Lookup.Kind, entry.Version);
}

/// <summary>One template with its text, with no template id (D224).</summary>
internal sealed record TemplateResponse(
    string MessageType, string Language, string? CompanyId, string Part, TemplatePartKind Kind, long Version, string Text)
{
    public static TemplateResponse From(TemplateLookup lookup, TemplateSource source) =>
        new(lookup.Type.Name, lookup.Language, lookup.Scope, lookup.Part, lookup.Kind, source.Key.Version, source.Text);
}

/// <summary>The body of a check of a draft: the text (D241).</summary>
internal sealed record TemplateCheckRequest(string Text);

/// <summary>One issue of a checked text, with the line and the column of the engine, or null (D241).</summary>
internal sealed record TemplateIssueResponse(string Message, int? Line, int? Column)
{
    public static TemplateIssueResponse From(TemplateIssue issue) => new(issue.Message, issue.Line, issue.Column);
}

/// <summary>One declared part of a message type, with the names of the engines that read it (D240).</summary>
internal sealed record TemplatePartResponse(string MessageType, string Part, TemplatePartKind Kind, IReadOnlyList<string> Engines)
{
    public static TemplatePartResponse From(Management.TemplatePartInfo info) => new(info.Type.Name, info.Part.Name, info.Part.Kind, info.Engines);
}
