namespace MsgHatch.AspNetCore;

/// <summary>The body of <c>PUT {prefix}/channels/{channel}</c> (section 4.20).</summary>
/// <param name="Enabled">False when the pipeline skips each route on the channel.</param>
/// <param name="Revision">The expected revision of the stored entry, or 0 for a new entry (D79).</param>
/// <param name="DisplayName">The name that replaces the name of the code, or null for the name of the code.</param>
/// <param name="Description">The description, or null for the description of the code.</param>
public sealed record ChannelEntryRequest(bool Enabled, int Revision, string? DisplayName = null, string? Description = null);

/// <summary>The body of <c>PUT {prefix}/message-types/{type}</c> (section 4.20).</summary>
/// <param name="Enabled">False when the pipeline stores nothing for the message type.</param>
/// <param name="Revision">The expected revision of the stored entry, or 0 for a new entry (D79).</param>
/// <param name="DisplayName">The display name, or null for the display name of the code.</param>
/// <param name="Description">The description, or null for the description of the code.</param>
/// <param name="DisabledChannels">The names of the channels of the message type on which the pipeline skips its routes, or null for none.</param>
public sealed record MessageTypeEntryRequest(
    bool Enabled, int Revision, string? DisplayName = null, string? Description = null, IReadOnlyList<string>? DisabledChannels = null);
