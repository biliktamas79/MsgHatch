using MsgHatch.Domain.Routing;

namespace MsgHatch.AspNetCore;

/// <summary>One channel in the body of <c>GET {prefix}/channels</c> (section 4.20).</summary>
/// <param name="Id">The id of the channel, for example <c>Email</c>.</param>
/// <param name="Name">The display name, from the entry or from the code.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Features">The features, as one text (D155).</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
/// <param name="Revision">The revision of the stored entry, or 0 for no entry.</param>
public sealed record RegistryChannelResponse(
    string Id, string Name, string? Description, ChannelFeatures Features, bool Enabled, int Revision)
{
    internal static RegistryChannelResponse From(ChannelDescriptor channel, int revision) =>
        new(channel.Id.Name, channel.Name, channel.Description, channel.Features, channel.Enabled, revision);
}

/// <summary>One message type in the body of <c>GET {prefix}/message-types</c> (section 4.20).</summary>
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
    internal static RegistryMessageTypeResponse From(MessageTypeDescriptor type, int revision) =>
        new(
            type.Type.Name, type.DisplayName, type.Description, [.. type.Channels.Select(static channel => channel.Name)], type.Enabled,
            [.. (type.DisabledChannels ?? []).Select(static channel => channel.Name)], revision);
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
    internal static ChannelEntryResponse From(ChannelEntry entry) =>
        new(entry.Channel.Name, entry.DisplayName, entry.Description, entry.Enabled, entry.Revision, entry.LastModifiedAtUtc);
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
    internal static MessageTypeEntryResponse From(MessageTypeEntry entry) =>
        new(
            entry.Type.Name, entry.DisplayName, entry.Description, entry.Enabled,
            [.. entry.DisabledChannels.Select(static channel => channel.Name)], entry.Revision, entry.LastModifiedAtUtc);
}
