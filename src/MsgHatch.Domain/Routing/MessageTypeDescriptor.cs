using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>One registered message type, with its event type and its channels.</summary>
/// <param name="Type">The message type.</param>
/// <param name="EventType">The type of the domain event.</param>
/// <param name="Channels">The channels of the registrations of the message type.</param>
/// <param name="DisplayName">An optional display name.</param>
/// <param name="Description">An optional description.</param>
/// <param name="Enabled">False when the pipeline stores nothing for the message type (D118).</param>
/// <param name="DisabledChannels">The channels on which the pipeline skips the routes of the message type, or null.</param>
public sealed record MessageTypeDescriptor(
    MessageType Type, Type EventType, IReadOnlyList<ChannelId> Channels,
    string? DisplayName = null, string? Description = null, bool Enabled = true,
    IReadOnlyList<ChannelId>? DisabledChannels = null);
