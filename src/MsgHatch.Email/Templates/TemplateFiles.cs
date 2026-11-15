namespace MsgHatch.Email.Templates;

/// <summary>
/// The names of the template files of the library stores (section 4.10): <c>{MessageType}.{Language}</c> with the part
/// suffix <c>.subject.txt</c>, <c>.body.html</c>, or <c>.body.txt</c>, and the template of a scope in the folder of
/// the scope (D199, <see cref="ScopeFolders"/>). The language is escaped with <see cref="Uri.EscapeDataString(string)"/>,
/// so that a name never leaves its folder.
/// </summary>
internal static class TemplateFiles
{
    public static readonly (string Name, TemplatePartKind Kind, string Suffix)[] Parts =
    [
        ("subject", TemplatePartKind.PlainText, ".subject.txt"),
        ("html", TemplatePartKind.Html, ".body.html"),
        ("text", TemplatePartKind.PlainText, ".body.txt"),
    ];

    /// <summary>Gives the path of a template with no part suffix, or null when its scope has no folder.</summary>
    public static string? PathOf(string rootPath, TemplateLookup lookup) =>
        lookup.Scope is null ? Path.Combine(rootPath, BaseName(lookup))
        : ScopeFolders.FolderOf(lookup.Scope) is { } folder ? Path.Combine(rootPath, folder, BaseName(lookup))
        : null;

    /// <summary>Gives the resource name of a template with no part suffix, or null when its scope has no folder.</summary>
    public static string? ResourceNameOf(string prefix, TemplateLookup lookup) =>
        lookup.Scope is null ? $"{prefix}.{BaseName(lookup)}"
        : ScopeFolders.SegmentOf(lookup.Scope) is { } segment ? $"{prefix}.{segment}.{BaseName(lookup)}"
        : null;

    private static string BaseName(TemplateLookup lookup) => $"{lookup.Type.Name}.{Uri.EscapeDataString(lookup.Language)}";
}
