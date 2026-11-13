using System.Text.Json;

namespace MsgHatch.Data.FileSystem;

/// <summary>
/// Reads a name id, for example a channel, from a JSON string. A null or invalid name is invalid JSON, so the file
/// boundary gives <c>Errors.SerializationFailed</c> for it.
/// </summary>
internal static class JsonNames
{
    public static T Read<T>(string? name, Func<string, T> create)
    {
        try
        {
            return create(name ?? throw new JsonException("A name is null."));
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }
}
