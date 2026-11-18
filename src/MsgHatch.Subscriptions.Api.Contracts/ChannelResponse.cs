using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Api.Contracts;

/// <summary>One registered channel in the body of <c>GET {prefix}/channels</c> (D77).</summary>
/// <param name="Id">The id of the channel, for example <c>Email</c>.</param>
/// <param name="Name">The display name.</param>
/// <param name="Features">The features, as one text, for example <c>"Languages, RichText"</c> (D155).</param>
/// <param name="Description">The optional description.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
public sealed record ChannelResponse(string Id, string Name, ChannelFeatures Features, string? Description, bool Enabled)
{
    /// <summary>Gives the body of a channel descriptor (D228).</summary>
    /// <param name="channel">The channel descriptor.</param>
    /// <returns>The body.</returns>
    public static ChannelResponse From(ChannelDescriptor channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return new ChannelResponse(channel.Id.Name, channel.Name, channel.Features, channel.Description, channel.Enabled);
    }

    /// <summary>
    /// Gives the channel descriptor of the body, with the settings type of the newest entry <c>Settings:{channel}</c> of the
    /// registry of the host, or <see cref="ChannelSettings"/> when the host registers none (D229). A value that is not valid
    /// gives <see cref="Errors.SerializationFailed"/>.
    /// </summary>
    /// <param name="registry">The content type registry, which gives the settings type of the channel.</param>
    /// <returns>The channel descriptor, or an error.</returns>
    public ErrorOr<ChannelDescriptor> ToChannelDescriptor(IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var id = WireValues.ChannelOf(Id);
        return id.IsError ? id.Error
            : Name is null ? WireValues.Invalid("A channel has a name.")
            : new WireChannelDescriptor(id.Value, Name, Features, Description, Enabled, SettingsJson.SettingsTypeOf(registry, id.Value));
    }

    private sealed record WireChannelDescriptor(
        Domain.Identity.ChannelId Id, string Name, ChannelFeatures Features, string? Description, bool Enabled, Type Settings)
        : ChannelDescriptor(Id, Name, Features, Description, Enabled)
    {
        public override Type SettingsType => Settings;
    }
}
