using MsgHatch.Domain.Routing;

namespace MsgHatch.Api.Contracts;

/// <summary>The body of <c>PUT {prefix}/channels/{channel}</c> (section 4.20).</summary>
/// <param name="Enabled">False when the pipeline skips each route on the channel.</param>
/// <param name="Revision">The expected revision of the stored entry, or 0 for a new entry (D79).</param>
/// <param name="DisplayName">The name that replaces the name of the code, or null for the name of the code.</param>
/// <param name="Description">The description, or null for the description of the code.</param>
public sealed record ChannelEntryRequest(bool Enabled, int Revision, string? DisplayName = null, string? Description = null)
{
    /// <summary>Gives the body of a save of a channel entry (D228, D284). The channel goes into the path.</summary>
    /// <param name="entry">The entry with the expected revision.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The entry is null.</exception>
    public static ChannelEntryRequest From(ChannelEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ChannelEntryRequest(entry.Enabled, entry.Revision, entry.DisplayName, entry.Description);
    }
}

/// <summary>The body of <c>PUT {prefix}/message-types/{type}</c> (section 4.20).</summary>
/// <param name="Enabled">False when the pipeline stores nothing for the message type.</param>
/// <param name="Revision">The expected revision of the stored entry, or 0 for a new entry (D79).</param>
/// <param name="DisplayName">The display name, or null for the display name of the code.</param>
/// <param name="Description">The description, or null for the description of the code.</param>
/// <param name="DisabledChannels">The names of the channels of the message type on which the pipeline skips its routes, or null for none.</param>
public sealed record MessageTypeEntryRequest(
    bool Enabled, int Revision, string? DisplayName = null, string? Description = null, IReadOnlyList<string>? DisabledChannels = null)
{
    /// <summary>Gives the body of a save of a message type entry (D228, D284). The message type goes into the path.</summary>
    /// <param name="entry">The entry with the expected revision.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The entry is null.</exception>
    public static MessageTypeEntryRequest From(MessageTypeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new MessageTypeEntryRequest(
            entry.Enabled, entry.Revision, entry.DisplayName, entry.Description, [.. entry.DisabledChannels.Select(static channel => channel.Name)]);
    }
}
