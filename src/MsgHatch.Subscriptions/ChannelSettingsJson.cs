using System.Collections.Frozen;
using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions;

/// <summary>
/// Writes and reads the channel settings of a subscription by the content type <c>Settings:{Channel}</c> and its version
/// (D77, D149), as <see cref="MsgHatchJson"/> does for the messages. Each subscription store uses it, also the store of a
/// consumer. It is thread-safe.
/// </summary>
public sealed class ChannelSettingsJson
{
    private const string _settingsPrefix = "Settings:";

    private readonly IContentTypeRegistry _registry;
    private readonly FrozenDictionary<string, ContentTypeEntry> _newestEntries;

    /// <summary>Makes the serializer for the settings entries of a content type registry.</summary>
    /// <param name="registry">The content type registry.</param>
    public ChannelSettingsJson(IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _newestEntries = registry.Entries
            .Where(static entry => entry.ContentType.StartsWith(_settingsPrefix, StringComparison.Ordinal))
            .GroupBy(static entry => entry.ContentType, StringComparer.Ordinal)
            .ToFrozenDictionary(
                static group => group.Key, static group => group.MaxBy(static entry => entry.Version)!, StringComparer.Ordinal);
    }

    /// <summary>Writes the settings of a channel with the newest version of the content type of that channel.</summary>
    /// <param name="channel">The channel of the subscription.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>
    /// The content type, the version, and the JSON. An unregistered channel, or settings of another type than the settings
    /// type of the channel, give <see cref="Errors.SerializationFailed"/>.
    /// </returns>
    public ErrorOr<SerializedSettings> Serialize(ChannelId channel, ChannelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var contentType = _settingsPrefix + channel.Name;
        return _newestEntries.TryGetValue(contentType, out var entry) && entry.ClrType == settings.GetType()
            ? new SerializedSettings(contentType, entry.Version, JsonSerializer.Serialize(settings, entry.ContentJsonTypeInfo))
            : new Error(Errors.SerializationFailed, $"{settings.GetType().Name} is not the registered settings type of the channel {channel.Name}.");
    }

    /// <summary>Reads settings as the registered settings type of their content type and version.</summary>
    /// <param name="contentType">The content type, for example <c>Settings:Email</c>.</param>
    /// <param name="version">The version.</param>
    /// <param name="json">The JSON of the settings.</param>
    /// <returns>
    /// The settings. A version that is newer than the code gives <see cref="Errors.UnsupportedVersion"/>. An unknown
    /// content type, a content type that is not a settings type, or invalid JSON gives <see cref="Errors.SerializationFailed"/>.
    /// </returns>
    public ErrorOr<ChannelSettings> Deserialize(string contentType, int version, string json)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(json);
        if (!_registry.TryFind(contentType, version, out var entry))
        {
            return IsNewerThanTheCode(contentType, version)
                ? new Error(Errors.UnsupportedVersion, $"The version {version} of {contentType} is newer than the code.")
                : new Error(Errors.SerializationFailed, $"{contentType} version {version} is not a registered settings type.");
        }

        try
        {
            return JsonSerializer.Deserialize(json, entry.ContentJsonTypeInfo) is ChannelSettings settings
                ? settings
                : new Error(Errors.SerializationFailed, $"The settings of {contentType} are null.");
        }
        catch (JsonException exception)
        {
            return new Error(Errors.SerializationFailed, exception.Message, exception);
        }
    }

    private bool IsNewerThanTheCode(string contentType, int version) =>
        _newestEntries.TryGetValue(contentType, out var newest) && newest.Version < version;
}
