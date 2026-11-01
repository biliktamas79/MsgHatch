using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>The description of a channel (D77). <see cref="ChannelDescriptor{TSettings}"/> gives its settings type.</summary>
/// <param name="Id">The id of the channel.</param>
/// <param name="Name">The display name of the channel.</param>
/// <param name="Features">The features of the channel.</param>
/// <param name="Description">An optional description.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
public abstract record ChannelDescriptor(
    ChannelId Id, string Name, ChannelFeatures Features, string? Description = null, bool Enabled = true)
{
    /// <summary>The <see cref="ChannelSettings"/> type of each route on the channel.</summary>
    public abstract Type SettingsType { get; }
}

/// <summary>The description of a channel with the settings type <typeparamref name="TSettings"/> (D77).</summary>
/// <typeparam name="TSettings">The <see cref="ChannelSettings"/> type of each route on the channel.</typeparam>
/// <param name="Id">The id of the channel.</param>
/// <param name="Name">The display name of the channel.</param>
/// <param name="Features">The features of the channel.</param>
/// <param name="Description">An optional description.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
public sealed record ChannelDescriptor<TSettings>(
    ChannelId Id, string Name, ChannelFeatures Features, string? Description = null, bool Enabled = true)
    : ChannelDescriptor(Id, Name, Features, Description, Enabled)
    where TSettings : ChannelSettings
{
    /// <inheritdoc/>
    public override Type SettingsType => typeof(TSettings);
}
