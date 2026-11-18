using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Api.Contracts;

/// <summary>One channel in the body of <c>GET {prefix}/channels</c> (section 4.20, D270).</summary>
/// <param name="Id">The id of the channel, for example <c>Email</c>.</param>
/// <param name="Name">The display name, from the entry or from the code.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Features">The features, as one text (D155).</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
/// <param name="Revision">The revision of the stored entry, or 0 for no entry.</param>
public sealed record RegistryChannelResponse(
    string Id, string Name, string? Description, ChannelFeatures Features, bool Enabled, int Revision)
{
    /// <summary>Gives the body of a channel of the registry queries (D228).</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The channel is null.</exception>
    public static RegistryChannelResponse From(RegistryChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return new RegistryChannelResponse(channel.Id.Name, channel.Name, channel.Description, channel.Features, channel.Enabled, channel.Revision);
    }

    /// <summary>Gives the channel of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The channel, or an error.</returns>
    public ErrorOr<RegistryChannel> ToChannel()
    {
        var id = RegistryWireValues.ChannelOf(Id);
        return id.IsError ? id.Error
            : Name is null ? RegistryWireValues.Invalid("A channel has a name.")
            : new RegistryChannel(id.Value, Name, Description, Features, Enabled, Revision);
    }
}

/// <summary>One message type in the body of <c>GET {prefix}/message-types</c> (section 4.20, D270).</summary>
/// <param name="Name">The message type name.</param>
/// <param name="DisplayName">The display name, or null.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Channels">The names of the registered channels of the message type.</param>
/// <param name="Enabled">False when the pipeline stores nothing for the message type (D118).</param>
/// <param name="DisabledChannels">The names of the channels on which the pipeline skips its routes.</param>
/// <param name="Revision">The revision of the stored entry, or 0 for no entry.</param>
public sealed record RegistryMessageTypeResponse(
    string Name, string? DisplayName, string? Description, IReadOnlyList<string> Channels, bool Enabled,
    IReadOnlyList<string> DisabledChannels, int Revision)
{
    /// <summary>Gives the body of a message type of the registry queries (D228).</summary>
    /// <param name="type">The message type.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The message type is null.</exception>
    public static RegistryMessageTypeResponse From(RegistryMessageType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new RegistryMessageTypeResponse(
            type.Type.Name, type.DisplayName, type.Description, [.. type.Channels.Select(static channel => channel.Name)], type.Enabled,
            [.. type.DisabledChannels.Select(static channel => channel.Name)], type.Revision);
    }

    /// <summary>Gives the message type of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The message type, or an error.</returns>
    public ErrorOr<RegistryMessageType> ToMessageType()
    {
        var type = RegistryWireValues.MessageTypeOf(Name);
        var channels = RegistryWireValues.ChannelsOf(Channels);
        var disabled = RegistryWireValues.ChannelsOf(DisabledChannels);
        return (type.Error ?? channels.Error ?? disabled.Error) is { } invalid
            ? invalid
            : new RegistryMessageType(type.Value, DisplayName, Description, channels.Value, Enabled, disabled.Value, Revision);
    }
}

/// <summary>The saved entry of a channel in the body of the response of <c>PUT {prefix}/channels/{channel}</c>.</summary>
/// <param name="Channel">The id of the channel.</param>
/// <param name="DisplayName">The display name, or null for the name of the code.</param>
/// <param name="Description">The description, or null for the description of the code.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel.</param>
/// <param name="Revision">The new revision of the entry.</param>
/// <param name="LastModifiedAtUtc">The time of the save.</param>
public sealed record ChannelEntryResponse(
    string Channel, string? DisplayName, string? Description, bool Enabled, int Revision, DateTime LastModifiedAtUtc)
{
    /// <summary>Gives the body of a saved entry (D228).</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The entry is null.</exception>
    public static ChannelEntryResponse From(ChannelEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ChannelEntryResponse(entry.Channel.Name, entry.DisplayName, entry.Description, entry.Enabled, entry.Revision, entry.LastModifiedAtUtc);
    }
}

/// <summary>The saved entry of a message type in the body of the response of <c>PUT {prefix}/message-types/{type}</c>.</summary>
/// <param name="Type">The message type name.</param>
/// <param name="DisplayName">The display name, or null for the display name of the code.</param>
/// <param name="Description">The description, or null for the description of the code.</param>
/// <param name="Enabled">False when the pipeline stores nothing for the message type.</param>
/// <param name="DisabledChannels">The names of the channels on which the pipeline skips its routes.</param>
/// <param name="Revision">The new revision of the entry.</param>
/// <param name="LastModifiedAtUtc">The time of the save.</param>
public sealed record MessageTypeEntryResponse(
    string Type, string? DisplayName, string? Description, bool Enabled, IReadOnlyList<string> DisabledChannels, int Revision,
    DateTime LastModifiedAtUtc)
{
    /// <summary>Gives the body of a saved entry (D228).</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The entry is null.</exception>
    public static MessageTypeEntryResponse From(MessageTypeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new MessageTypeEntryResponse(
            entry.Type.Name, entry.DisplayName, entry.Description, entry.Enabled,
            [.. entry.DisabledChannels.Select(static channel => channel.Name)], entry.Revision, entry.LastModifiedAtUtc);
    }
}

/// <summary>The checks of the names in a registry body, which give <see cref="Errors.SerializationFailed"/> (D228).</summary>
internal static class RegistryWireValues
{
    public static Error Invalid(string message) => new(Errors.SerializationFailed, message);

    public static ErrorOr<ChannelId> ChannelOf(string? name) => NameOf(name, "channel", static valid => new ChannelId(valid));

    public static ErrorOr<MessageType> MessageTypeOf(string? name) => NameOf(name, "message type", static valid => new MessageType(valid));

    public static ErrorOr<IReadOnlyList<ChannelId>> ChannelsOf(IReadOnlyList<string>? names)
    {
        if (names is null)
        {
            return Invalid("A message type has a list of channels and a list of disabled channels.");
        }

        var channels = new ChannelId[names.Count];
        for (var index = 0; index < channels.Length; index++)
        {
            var channel = ChannelOf(names[index]);
            if (channel.IsError)
            {
                return channel.Error;
            }

            channels[index] = channel.Value;
        }

        return channels;
    }

    private static ErrorOr<T> NameOf<T>(string? name, string kind, Func<string, T> create)
    {
        if (name is null)
        {
            return Invalid($"A {kind} has a name.");
        }

        try
        {
            return create(name);
        }
        catch (ArgumentException exception)
        {
            return Invalid($"The {kind} name is not valid: {exception.Message}");
        }
    }
}
