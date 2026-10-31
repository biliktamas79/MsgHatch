using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MsgHatch.Domain.Identity;

internal static class StringIds
{
    public const int MaxLength = 256;

    public static bool IsValid([NotNullWhen(true)] string? value) =>
        value is { Length: > 0 and <= MaxLength } && !ContainsControlCharacter(value);

    public static string Validate(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return IsValid(value)
            ? value
            : throw new ArgumentException("An id is not empty, has at most 256 characters, and has no control characters.", paramName);
    }

    public static string Format<T>(T id) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[64];
        return id.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture)
            ? new string(buffer[..written])
            : id.ToString(null, CultureInfo.InvariantCulture);
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }
}
