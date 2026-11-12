using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.AspNetCore;

/// <summary>
/// Reads and writes the <c>settings</c> object of a body with the JSON type information of the settings type of the
/// channel, from the content type registry (section 4.17).
/// </summary>
internal static class SettingsJson
{
    public static ErrorOr<ChannelSettings> Read(IContentTypeRegistry registry, ChannelId channel, JsonElement json)
    {
        var contentType = "Settings:" + channel.Name;
        if (registry.Entries.Where(entry => entry.ContentType == contentType).MaxBy(static entry => entry.Version) is not { } newest)
        {
            return new Error(Errors.ValidationFailed, $"The channel {channel.Name} has no registered settings type.");
        }

        try
        {
            return (ChannelSettings)json.Deserialize(newest.ContentJsonTypeInfo)!;
        }
        catch (JsonException exception)
        {
            return new Error(Errors.ValidationFailed, exception.Message);
        }
    }

    /// <summary>Writes a value of a registered content type, for example channel settings or a route trace.</summary>
    public static JsonElement Write(IContentTypeRegistry registry, object value)
    {
        var type = value.GetType();
        var newest = registry.Entries.Where(entry => entry.ClrType == type).MaxBy(static entry => entry.Version)
            ?? throw new InvalidOperationException($"{type.Name} is not a registered content type.");
        return JsonSerializer.SerializeToElement(value, newest.ContentJsonTypeInfo);
    }
}
