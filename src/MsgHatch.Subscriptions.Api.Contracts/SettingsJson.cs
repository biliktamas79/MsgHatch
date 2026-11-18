using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Api.Contracts;

/// <summary>
/// Reads and writes the <c>settings</c> object of a body with the JSON type information of the newest settings type of
/// the channel, from the content type registry (section 4.17, D228, D229).
/// </summary>
public static class SettingsJson
{
    /// <summary>
    /// Reads the settings of a request on the server. A channel with no registered settings type, and JSON that the
    /// settings type cannot read, give <see cref="Errors.ValidationFailed"/>.
    /// </summary>
    /// <param name="registry">The content type registry.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="json">The JSON of the settings.</param>
    /// <returns>The settings, or an error.</returns>
    public static ErrorOr<ChannelSettings> Read(IContentTypeRegistry registry, ChannelId channel, JsonElement json)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (NewestSettingsEntry(registry, channel) is not { } newest)
        {
            return new Error(Errors.ValidationFailed, $"The channel {channel.Name} has no registered settings type.");
        }

        var settings = Deserialize(newest, json);
        return settings.IsError ? new Error(Errors.ValidationFailed, settings.Error.Message) : settings;
    }

    /// <summary>
    /// Writes a value of a registered content type, for example channel settings or a route trace. Writes the JSON of
    /// <see cref="JsonChannelSettings"/> with no change.
    /// </summary>
    /// <param name="registry">The content type registry.</param>
    /// <param name="value">The value.</param>
    /// <returns>The JSON.</returns>
    /// <exception cref="InvalidOperationException">The type of the value is not a registered content type.</exception>
    public static JsonElement Write(IContentTypeRegistry registry, object value)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(value);
        if (value is JsonChannelSettings json)
        {
            return json.Json;
        }

        var type = value.GetType();
        var newest = registry.Entries.Where(entry => entry.ClrType == type).MaxBy(static entry => entry.Version)
            ?? throw new InvalidOperationException($"{type.Name} is not a registered content type.");
        return JsonSerializer.SerializeToElement(value, newest.ContentJsonTypeInfo);
    }

    /// <summary>
    /// Reads the settings of a body from the wire: <see cref="JsonChannelSettings"/> for a channel with no registered
    /// settings type, and <see cref="Errors.SerializationFailed"/> for JSON that the settings type cannot read.
    /// </summary>
    internal static ErrorOr<ChannelSettings> ReadFromTheWire(IContentTypeRegistry registry, ChannelId channel, JsonElement json) =>
        NewestSettingsEntry(registry, channel) is { } newest
            ? Deserialize(newest, json)
            : new JsonChannelSettings(json.Clone());

    /// <summary>Gives the settings type of the newest registered settings entry of a channel, or <see cref="ChannelSettings"/>.</summary>
    internal static Type SettingsTypeOf(IContentTypeRegistry registry, ChannelId channel) =>
        NewestSettingsEntry(registry, channel)?.ClrType ?? typeof(ChannelSettings);

    private static ContentTypeEntry? NewestSettingsEntry(IContentTypeRegistry registry, ChannelId channel)
    {
        var contentType = "Settings:" + channel.Name;
        return registry.Entries.Where(entry => entry.ContentType == contentType).MaxBy(static entry => entry.Version);
    }

    private static ErrorOr<ChannelSettings> Deserialize(ContentTypeEntry entry, JsonElement json)
    {
        try
        {
            return json.Deserialize(entry.ContentJsonTypeInfo) is ChannelSettings settings
                ? settings
                : WireValues.Invalid("The settings are the JSON null.");
        }
        catch (JsonException exception)
        {
            return WireValues.Invalid(exception.Message);
        }
    }
}
