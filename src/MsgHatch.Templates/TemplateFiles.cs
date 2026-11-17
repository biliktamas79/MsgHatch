using MsgHatch.Domain.Identity;

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

    /// <summary>
    /// Gives the lookup of a file name in the folder of a scope, the reverse of the file name of a lookup. A name that the
    /// library does not make from the parts of a lookup, for example the temporary file of a save, has no lookup.
    /// </summary>
    public static TemplateLookup? LookupOf(string fileName, string? scope)
    {
        var parts = fileName.Split('.');
        if (parts.Length != 4 || parts[1].Length == 0 || parts[2].Length == 0 || KindOf(parts[3]) is not { } kind || TypeOf(parts[0]) is not { } type)
        {
            return null;
        }

        var lookup = new TemplateLookup(type, Uri.UnescapeDataString(parts[1]), scope, Uri.UnescapeDataString(parts[2]), kind);
        return FileNameOf(lookup) == fileName ? lookup : null;
    }

    private static TemplatePartKind? KindOf(string extension) =>
        extension switch
        {
            "txt" => TemplatePartKind.PlainText,
            "html" => TemplatePartKind.Html,
            _ => null,
        };

    private static MessageType? TypeOf(string name)
    {
        try
        {
            return new MessageType(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string FileNameOf(TemplateLookup lookup) =>
        $"{lookup.Type.Name}.{Uri.EscapeDataString(lookup.Language)}.{Uri.EscapeDataString(lookup.Part)}.{(lookup.Kind == TemplatePartKind.Html ? "html" : "txt")}";
}
