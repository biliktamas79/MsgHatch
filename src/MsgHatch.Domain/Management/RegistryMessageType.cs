using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>
/// One message type of <see cref="IRegistryQueries"/> (D268): the values of <see cref="IMessageRegistry"/>, with the
/// stored settings on top, and the revision of the stored entry. It has no event type, so it can go over HTTP.
/// </summary>
/// <param name="Type">The message type.</param>
/// <param name="DisplayName">The display name, from the stored entry or from the code, or null.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Channels">The channels of the registrations of the message type.</param>
/// <param name="Enabled">False when the pipeline stores nothing for the message type (D118).</param>
/// <param name="DisabledChannels">The channels on which the pipeline skips the routes of the message type.</param>
/// <param name="Revision">The revision of the stored entry, or 0 for no entry.</param>
/// <param name="Entry">
/// The stored entry of the message type, or null for no entry and in a host with no registry writer (D286). A null value of
/// the entry is the value of the code.
/// </param>
public sealed record RegistryMessageType(
    MessageType Type, string? DisplayName, string? Description, IReadOnlyList<ChannelId> Channels, bool Enabled,
    IReadOnlyList<ChannelId> DisabledChannels, int Revision, MessageTypeEntry? Entry = null);
