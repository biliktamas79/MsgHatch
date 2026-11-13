using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>The stored registry settings of one channel, on top of the code (D118, section 4.20).</summary>
/// <param name="Channel">The id of the channel.</param>
/// <param name="DisplayName">The name that replaces <see cref="ChannelDescriptor.Name"/>, or null for the name of the code.</param>
/// <param name="Description">The description, or null for the description of the code.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel.</param>
/// <param name="Revision">The revision of the entry (D79).</param>
/// <param name="LastModifiedAtUtc">The time of the last save.</param>
public sealed record ChannelEntry(
    ChannelId Channel, string? DisplayName, string? Description, bool Enabled, int Revision, DateTime LastModifiedAtUtc);
