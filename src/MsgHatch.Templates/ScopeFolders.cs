using System.Buffers;

namespace MsgHatch.Templates;

/// <summary>
/// The folder of the templates of a template scope, the company of D80 (D199, D201). In a file store, each scope that
/// is not empty has a folder: its name escaped, so that a company id such as a GUID has its templates, two ids never
/// share a folder, and no name leads out of its folder. In the name of an embedded resource, MSBuild changes each
/// character that is not valid in a namespace, so only a scope of ASCII letters, digits, and <c>_</c>, which does not
/// start with <c>_</c>, has a folder there, with <c>_</c> before a leading digit. Each other scope has no folder.
/// </summary>
public static class ScopeFolders
{
    private static readonly SearchValues<char> _segmentCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");

    /// <summary>
    /// Gives the folder name of a scope in a file store: escaped with <see cref="Uri.EscapeDataString(string)"/>, with
    /// each <c>.</c> as <c>%2E</c>, and each capital letter A to Z as <c>^</c> and the small letter, because Windows and
    /// macOS ignore the case of a folder name, but a company id is case-sensitive.
    /// </summary>
    /// <param name="scope">The template scope.</param>
    /// <returns>The folder name, or null when the scope is empty.</returns>
    public static string? FolderOf(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Length == 0 ? null : string.Concat(Uri.EscapeDataString(scope).Select(static character => character switch
        {
            '.' => "%2E",
            _ when char.IsAsciiLetterUpper(character) => $"^{char.ToLowerInvariant(character)}",
            _ => character.ToString(),
        }));
    }

    /// <summary>Gives the folder of a scope as a part of the name of an embedded resource.</summary>
    /// <param name="scope">The template scope.</param>
    /// <returns>The part, or null when the scope has no folder there.</returns>
    public static string? SegmentOf(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Length == 0 || scope[0] == '_' || scope.AsSpan().ContainsAnyExcept(_segmentCharacters) ? null
            : char.IsAsciiDigit(scope[0]) ? "_" + scope
            : scope;
    }
}
