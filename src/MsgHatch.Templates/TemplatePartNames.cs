using System.Buffers;

namespace MsgHatch.Templates;

/// <summary>The rule of a part name (D220): ASCII letters, digits, and <c>_</c>, which starts with a letter.</summary>
internal static class TemplatePartNames
{
    private static readonly SearchValues<char> _characters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");

    public static bool IsValid(string? name) =>
        name is { Length: > 0 } && char.IsAsciiLetter(name[0]) && !name.AsSpan().ContainsAnyExcept(_characters);
}
