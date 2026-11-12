using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.AspNetCore;

/// <summary>One registered channel in the body of <c>GET {prefix}/channels</c> (D77).</summary>
/// <param name="Id">The id of the channel, for example <c>Email</c>.</param>
/// <param name="Name">The display name.</param>
/// <param name="Features">The features, as one text, for example <c>"Languages, RichText"</c> (D155).</param>
/// <param name="Description">The optional description.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
public sealed record ChannelResponse(string Id, string Name, ChannelFeatures Features, string? Description, bool Enabled)
{
    internal static ChannelResponse From(ChannelDescriptor channel) =>
        new(channel.Id.Name, channel.Name, channel.Features, channel.Description, channel.Enabled);
}
