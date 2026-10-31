using System.Buffers;

namespace MsgHatch.Domain.Identity;

internal static class IdentityNames
{
    private static readonly SearchValues<char> _nameCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");

    public static string Validate(string name, string paramName)
    {
        ArgumentNullException.ThrowIfNull(name, paramName);
        return IsValid(name)
            ? name
            : throw new ArgumentException("A name contains only ASCII letters, digits, and '_', and starts with a letter.", paramName);
    }

    public static bool IsValid(string name) =>
        name.Length > 0 && char.IsAsciiLetter(name[0]) && !name.AsSpan().ContainsAnyExcept(_nameCharacters);
}
