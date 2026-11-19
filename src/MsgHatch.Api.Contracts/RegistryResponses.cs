using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Api.Contracts;

/// <summary>One channel in the body of <c>GET {prefix}/channels</c> (section 4.20, D270, D278).</summary>
/// <param name="Id">The id of the channel, for example <c>Email</c>.</param>
/// <param name="Name">The display name, from the entry or from the code.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Features">The features, as one text (D155).</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
/// <param name="Revision">The revision of the stored entry, or 0 for no entry.</param>
/// <param name="Current">
/// The latest state row of the channel, or null for a channel with no row, which was never down. A body with no value is a
/// channel with no row.
/// </param>
/// <param name="Entry">The stored entry of the channel, or null for no entry (D286).</param>
public sealed record RegistryChannelResponse(
    string Id, string Name, string? Description, ChannelFeatures Features, bool Enabled, int Revision, ChannelStateResponse? Current = null,
    ChannelEntryResponse? Entry = null)
{
    /// <summary>Gives the body of a channel of the registry queries (D228).</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The channel is null.</exception>
    public static RegistryChannelResponse From(RegistryChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return new RegistryChannelResponse(
            channel.Id.Name, channel.Name, channel.Description, channel.Features, channel.Enabled, channel.Revision,
            channel.Current is { } current ? ChannelStateResponse.From(current) : null,
            channel.Entry is { } entry ? ChannelEntryResponse.From(entry) : null);
    }

    /// <summary>Gives the channel of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The channel, or an error.</returns>
    public ErrorOr<RegistryChannel> ToChannel()
    {
        var id = RegistryWireValues.ChannelOf(Id);
        var current = Current?.ToState();
        var entry = Entry?.ToEntry();
        return id.IsError ? id.Error
            : Name is null ? RegistryWireValues.Invalid("A channel has a name.")
            : current is { IsError: true } ? current.Value.Error
            : entry is { IsError: true } ? entry.Value.Error
            : new RegistryChannel(id.Value, Name, Description, Features, Enabled, Revision, current?.Value, entry?.Value);
    }
}

/// <summary>
/// The down state of a channel (D106), in the bodies of the registry channels and of the delivery channel status (D278).
/// </summary>
/// <param name="Reason">Why the channel is down.</param>
/// <param name="SinceUtc">The start of the down state.</param>
/// <param name="NextCheckAtUtc">The time of the next health check, only for the reason <c>Errors</c>.</param>
/// <param name="ErrorCode">The code of the last transient error, only for the reason <c>Errors</c>.</param>
/// <param name="ErrorName">The name of the last transient error, only for the reason <c>Errors</c>.</param>
/// <param name="Note">The text of the person, only for the reason <c>Operator</c>.</param>
public sealed record ChannelDownResponse(
    ChannelDownReason Reason, DateTime SinceUtc, DateTime? NextCheckAtUtc, int? ErrorCode, string? ErrorName, string? Note)
{
    /// <summary>Gives the body of a down state (D228).</summary>
    /// <param name="down">The down state.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The down state is null.</exception>
    public static ChannelDownResponse From(ChannelDown down)
    {
        ArgumentNullException.ThrowIfNull(down);
        return new ChannelDownResponse(down.Reason, down.SinceUtc, down.NextCheckAtUtc, down.ErrorCode, down.ErrorName, down.Note);
    }

    /// <summary>Gives the down state of the body for its channel.</summary>
    /// <param name="channel">The channel of the body that holds this body.</param>
    /// <returns>The down state.</returns>
    public ChannelDown ToDown(ChannelId channel) => new(channel, Reason, SinceUtc, NextCheckAtUtc, ErrorCode, ErrorName, Note);
}

/// <summary>One state row in the body of <c>GET {prefix}/channels/{channel}/history</c> (D277, D278).</summary>
/// <param name="Channel">The id of the channel.</param>
/// <param name="Version">The version of the row in its channel.</param>
/// <param name="Kind">Up or down.</param>
/// <param name="SinceUtc">The start of the state.</param>
/// <param name="PreviousKind">The kind of the previous state, or null for the version 1.</param>
/// <param name="PreviousDurationMs">The duration of the previous state in whole milliseconds, or null for the version 1.</param>
/// <param name="Reason">Why the channel is down, only for <c>Down</c>.</param>
/// <param name="NextCheckAtUtc">The time of the next health check, only for the reason <c>Errors</c>.</param>
/// <param name="ErrorCode">The code of the last transient error, only for the reason <c>Errors</c>.</param>
/// <param name="ErrorName">The name of the last transient error, only for the reason <c>Errors</c>.</param>
/// <param name="Note">The text of the person, only for the reason <c>Operator</c>.</param>
public sealed record ChannelStateResponse(
    string Channel, int Version, ChannelStateKind Kind, DateTime SinceUtc, ChannelStateKind? PreviousKind, long? PreviousDurationMs,
    ChannelDownReason? Reason, DateTime? NextCheckAtUtc, int? ErrorCode, string? ErrorName, string? Note)
{
    /// <summary>Gives the body of a state row (D228).</summary>
    /// <param name="row">The state row.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The row is null.</exception>
    public static ChannelStateResponse From(ChannelState row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new ChannelStateResponse(
            row.Channel.Name, row.Version, row.Kind, row.SinceUtc, row.PreviousKind, (long?)row.PreviousDuration?.TotalMilliseconds, row.Reason,
            row.NextCheckAtUtc, row.ErrorCode, row.ErrorName, row.Note);
    }

    /// <summary>
    /// Gives the state row of the body. A channel name that is not valid, or a <c>Down</c> row with no reason, gives
    /// <see cref="Errors.SerializationFailed"/> (D228).
    /// </summary>
    /// <returns>The state row, or an error.</returns>
    public ErrorOr<ChannelState> ToState()
    {
        var channel = RegistryWireValues.ChannelOf(Channel);
        return channel.IsError ? channel.Error
            : Kind == ChannelStateKind.Down && Reason is null ? RegistryWireValues.Invalid("A Down row has a reason.")
            : new ChannelState(
                channel.Value, Version, Kind, SinceUtc, PreviousKind,
                PreviousDurationMs is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : null, Reason, NextCheckAtUtc, ErrorCode, ErrorName, Note);
    }
}

/// <summary>The body of <c>POST {prefix}/channels/{channel}/check</c> (section 4.20, D283).</summary>
/// <param name="Channel">The channel name.</param>
/// <param name="ErrorCode">The code of the error of the health check, or null when the check gave no error.</param>
/// <param name="ErrorName">The name of the error of the health check, or null when the check gave no error.</param>
/// <param name="Changed">True when the check added a state row of the channel.</param>
public sealed record ChannelCheckResponse(string Channel, int? ErrorCode, string? ErrorName, bool Changed)
{
    /// <summary>Gives the body of a channel check (D228).</summary>
    /// <param name="check">The result of the check.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The check is null.</exception>
    public static ChannelCheckResponse From(ChannelCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return new ChannelCheckResponse(check.Channel.Name, check.ErrorCode, check.ErrorName, check.Changed);
    }

    /// <summary>
    /// Gives the check of the body. A channel name that is not valid, or an error code with no error name or an error name
    /// with no error code, gives <see cref="Errors.SerializationFailed"/> (D228).
    /// </summary>
    /// <returns>The check, or an error.</returns>
    public ErrorOr<ChannelCheck> ToCheck()
    {
        var channel = RegistryWireValues.ChannelOf(Channel);
        return channel.IsError ? channel.Error
            : ErrorCode is null != ErrorName is null ? RegistryWireValues.Invalid("A failed check has an error code and an error name.")
            : new ChannelCheck(channel.Value, ErrorCode, ErrorName, Changed);
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
/// <param name="Entry">The stored entry of the message type, or null for no entry (D286).</param>
public sealed record RegistryMessageTypeResponse(
    string Name, string? DisplayName, string? Description, IReadOnlyList<string> Channels, bool Enabled,
    IReadOnlyList<string> DisabledChannels, int Revision, MessageTypeEntryResponse? Entry = null)
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
            [.. type.DisabledChannels.Select(static channel => channel.Name)], type.Revision,
            type.Entry is { } entry ? MessageTypeEntryResponse.From(entry) : null);
    }

    /// <summary>Gives the message type of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The message type, or an error.</returns>
    public ErrorOr<RegistryMessageType> ToMessageType()
    {
        var type = RegistryWireValues.MessageTypeOf(Name);
        var channels = RegistryWireValues.ChannelsOf(Channels);
        var disabled = RegistryWireValues.ChannelsOf(DisabledChannels);
        var entry = Entry?.ToEntry();
        return (type.Error ?? channels.Error ?? disabled.Error ?? entry?.Error) is { } invalid
            ? invalid
            : new RegistryMessageType(type.Value, DisplayName, Description, channels.Value, Enabled, disabled.Value, Revision, entry?.Value);
    }
}

/// <summary>
/// The stored entry of a channel: the body of the response of <c>PUT {prefix}/channels/{channel}</c>, and the entry of a
/// channel in the body of <c>GET {prefix}/channels</c> (D286).
/// </summary>
/// <param name="Channel">The id of the channel.</param>
/// <param name="DisplayName">The display name, or null for the name of the code.</param>
/// <param name="Description">The description, or null for the description of the code.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel.</param>
/// <param name="Revision">The revision of the entry.</param>
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

    /// <summary>Gives the entry of the body. A channel name that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The entry, or an error.</returns>
    public ErrorOr<ChannelEntry> ToEntry()
    {
        var channel = RegistryWireValues.ChannelOf(Channel);
        return channel.IsError ? channel.Error : new ChannelEntry(channel.Value, DisplayName, Description, Enabled, Revision, LastModifiedAtUtc);
    }
}

/// <summary>
/// The stored entry of a message type: the body of the response of <c>PUT {prefix}/message-types/{type}</c>, and the entry of
/// a message type in the body of <c>GET {prefix}/message-types</c> (D286).
/// </summary>
/// <param name="Type">The message type name.</param>
/// <param name="DisplayName">The display name, or null for the display name of the code.</param>
/// <param name="Description">The description, or null for the description of the code.</param>
/// <param name="Enabled">False when the pipeline stores nothing for the message type.</param>
/// <param name="DisabledChannels">The names of the channels on which the pipeline skips its routes.</param>
/// <param name="Revision">The revision of the entry.</param>
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

    /// <summary>
    /// Gives the entry of the body. A message type name or a channel name that is not valid, or no list of disabled channels,
    /// gives <see cref="Errors.SerializationFailed"/> (D228).
    /// </summary>
    /// <returns>The entry, or an error.</returns>
    public ErrorOr<MessageTypeEntry> ToEntry()
    {
        var type = RegistryWireValues.MessageTypeOf(Type);
        var disabled = RegistryWireValues.ChannelsOf(DisabledChannels);
        return (type.Error ?? disabled.Error) is { } invalid
            ? invalid
            : new MessageTypeEntry(type.Value, DisplayName, Description, Enabled, disabled.Value, Revision, LastModifiedAtUtc);
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
