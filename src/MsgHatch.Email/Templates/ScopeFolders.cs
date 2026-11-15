using System.Buffers;

namespace MsgHatch.Email.Templates;

/// <summary>
/// The folder of the templates of a template scope, the company of D80 (D199, D201). A scope of ASCII letters, digits,
/// and <c>_</c>, which does not start with <c>_</c>, has the folder of its own name. In a namespace and in the name of
/// an embedded resource, the Razor SDK and MSBuild put <c>_</c> before a folder name that starts with a digit. Each
/// other scope has no folder. So no scope gets the templates of another scope, and no name leads out of its folder.
/// </summary>
public static class ScopeFolders
{
    private static readonly SearchValues<char> _folderCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");

    /// <summary>Gives the folder name of a scope.</summary>
    /// <param name="scope">The template scope.</param>
    /// <returns>The folder name, or null when the scope has no folder.</returns>
    public static string? FolderOf(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Length > 0 && scope[0] != '_' && !scope.AsSpan().ContainsAnyExcept(_folderCharacters) ? scope : null;
    }

    /// <summary>Gives the folder of a scope as a part of a namespace or of the name of an embedded resource.</summary>
    /// <param name="scope">The template scope.</param>
    /// <returns>The part, or null when the scope has no folder.</returns>
    public static string? SegmentOf(string scope) =>
        FolderOf(scope) is not { } folder ? null : char.IsAsciiDigit(folder[0]) ? "_" + folder : folder;
}
