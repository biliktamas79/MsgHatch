using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.SignalR;

/// <summary>The SignalR channel of the UI notifications (section 4.4).</summary>
public static class SignalRChannel
{
    /// <summary>The id of the SignalR channel.</summary>
    public static readonly ChannelId Id = new("SignalR");

    /// <summary>The descriptor of the SignalR channel, with languages, raw data, and group delivery (D77).</summary>
    public static readonly ChannelDescriptor<SignalRChannelSettings> Descriptor = new(
        Id, "UI notification", ChannelFeatures.Languages | ChannelFeatures.RawData | ChannelFeatures.GroupDelivery);
}
