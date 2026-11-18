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
    private const string _settingsPrefix = "Settings:";

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
    /// Reads the settings of a request on the server with the newest settings type of a content type, for example
    /// <c>Settings:Email</c>, so that the settings of the type of another channel reach the check of the manager (D247). A
    /// content type that is not a registered settings type, and JSON that the settings type cannot read, give
    /// <see cref="Errors.ValidationFailed"/>.
    /// </summary>
    /// <param name="registry">The content type registry.</param>
    /// <param name="settingsType">The content type of the settings.</param>
    /// <param name="json">The JSON of the settings.</param>
    /// <returns>The settings, or an error.</returns>
    public static ErrorOr<ChannelSettings> Read(IContentTypeRegistry registry, string settingsType, JsonElement json)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settingsType);
        if (NewestSettingsEntry(registry, settingsType) is not { } newest)
        {
            return new Error(Errors.ValidationFailed, $"The settings type {settingsType} is not a registered settings type.");
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
    /// Reads the settings of a body from the wire with the settings type of the body when the registry has it (D247), or else
    /// with the newest settings type of the channel: <see cref="JsonChannelSettings"/> for a channel with no registered
    /// settings type, and <see cref="Errors.SerializationFailed"/> for JSON that the settings type cannot read.
    /// </summary>
    internal static ErrorOr<ChannelSettings> ReadFromTheWire(IContentTypeRegistry registry, string? settingsType, ChannelId channel, JsonElement json) =>
        ((settingsType is null ? null : NewestSettingsEntry(registry, settingsType)) ?? NewestSettingsEntry(registry, channel)) is { } newest
            ? Deserialize(newest, json)
            : new JsonChannelSettings(json.Clone());

    /// <summary>Gives the content type of the newest settings entry of the type of the settings, or null for <see cref="JsonChannelSettings"/> (D247).</summary>
    internal static string? ContentTypeOf(IContentTypeRegistry registry, ChannelSettings settings) =>
        settings is JsonChannelSettings
            ? null
            : registry.Entries
                .Where(entry => entry.ClrType == settings.GetType() && entry.ContentType.StartsWith(_settingsPrefix, StringComparison.Ordinal))
                .MaxBy(static entry => entry.Version)?.ContentType;

    /// <summary>Gives the settings type of the newest registered settings entry of a channel, or <see cref="ChannelSettings"/>.</summary>
    internal static Type SettingsTypeOf(IContentTypeRegistry registry, ChannelId channel) =>
        NewestSettingsEntry(registry, channel)?.ClrType ?? typeof(ChannelSettings);

    private static ContentTypeEntry? NewestSettingsEntry(IContentTypeRegistry registry, ChannelId channel) =>
        NewestSettingsEntry(registry, _settingsPrefix + channel.Name);

    private static ContentTypeEntry? NewestSettingsEntry(IContentTypeRegistry registry, string contentType) =>
        contentType.StartsWith(_settingsPrefix, StringComparison.Ordinal)
            ? registry.Entries.Where(entry => entry.ContentType == contentType).MaxBy(static entry => entry.Version)
            : null;

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
