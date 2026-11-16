namespace MsgHatch.Templates;

/// <summary>
/// The names of the template files of the library stores (section 4.10, D214): <c>{MessageType}.{Language}.{Part}.txt</c>
/// or <c>.html</c>, and the template of a scope in the folder of the scope (D199, <see cref="ScopeFolders"/>). The
/// language and the part are escaped with <see cref="Uri.EscapeDataString(string)"/>, so that a name never leaves its folder.
/// </summary>
internal static class TemplateFiles
{
    /// <summary>Gives the path of the file of a lookup, or null when its scope has no folder.</summary>
    public static string? PathOf(string rootPath, TemplateLookup lookup) =>
        lookup.Scope is null ? Path.Combine(rootPath, FileNameOf(lookup))
        : ScopeFolders.FolderOf(lookup.Scope) is { } folder ? Path.Combine(rootPath, folder, FileNameOf(lookup))
        : null;

    /// <summary>Gives the resource name of the file of a lookup, or null when its scope has no folder.</summary>
    public static string? ResourceNameOf(string prefix, TemplateLookup lookup) =>
        lookup.Scope is null ? $"{prefix}.{FileNameOf(lookup)}"
        : ScopeFolders.SegmentOf(lookup.Scope) is { } segment ? $"{prefix}.{segment}.{FileNameOf(lookup)}"
        : null;

    private static string FileNameOf(TemplateLookup lookup) =>
        $"{lookup.Type.Name}.{Uri.EscapeDataString(lookup.Language)}.{Uri.EscapeDataString(lookup.Part)}.{(lookup.Kind == TemplatePartKind.Html ? "html" : "txt")}";
}
