using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>The description of a channel (D77).</summary>
/// <param name="Id">The id of the channel.</param>
/// <param name="Name">The display name of the channel.</param>
/// <param name="Features">The features of the channel.</param>
/// <param name="SettingsType">The <see cref="ChannelSettings"/> type of each route on the channel.</param>
/// <param name="Description">An optional description.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
public sealed record ChannelDescriptor(
    ChannelId Id, string Name, ChannelFeatures Features, Type SettingsType,
    string? Description = null, bool Enabled = true);
