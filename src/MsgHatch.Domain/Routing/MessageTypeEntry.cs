using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>The stored registry settings of one message type, on top of the code (D118, section 4.20).</summary>
/// <param name="Type">The message type.</param>
/// <param name="DisplayName">The display name, or null for the display name of the code.</param>
/// <param name="Description">The description, or null for the description of the code.</param>
/// <param name="Enabled">False when the pipeline stores nothing for the message type.</param>
/// <param name="DisabledChannels">The registered channels of the message type on which the pipeline skips its routes.</param>
/// <param name="Revision">The revision of the entry (D79).</param>
/// <param name="LastModifiedAtUtc">The time of the last save.</param>
public sealed record MessageTypeEntry(
    MessageType Type, string? DisplayName, string? Description, bool Enabled, IReadOnlyList<ChannelId> DisabledChannels,
    int Revision, DateTime LastModifiedAtUtc);
