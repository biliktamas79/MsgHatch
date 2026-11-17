namespace MsgHatch;

/// <summary>
/// An id in a file or folder name (section 4.5, D172, D201): escaped with <see cref="Uri.EscapeDataString(string)"/>,
/// which also escapes <c>^</c>, and each capital letter A to Z as <c>^</c> and the small letter. Windows and macOS
/// ignore the case of a name, but an id is case-sensitive. So two ids never give one name. Each package that names a
/// file or a folder after an id compiles this file.
/// </summary>
internal static class FileNames
{
    public static string Escape(string id) =>
        string.Concat(Uri.EscapeDataString(id).Select(static character =>
            char.IsAsciiLetterUpper(character) ? $"^{char.ToLowerInvariant(character)}" : character.ToString()));
}
