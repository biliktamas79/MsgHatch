using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Broker;

/// <summary>The broker channel of the broker messages (section 4.4, D134).</summary>
public static class BrokerChannel
{
    /// <summary>The id of the broker channel.</summary>
    public static readonly ChannelId Id = new("Broker");

    /// <summary>The descriptor of the broker channel, with raw data and group delivery (D77).</summary>
    public static readonly ChannelDescriptor<BrokerChannelSettings> Descriptor = new(
        Id, "Broker message", ChannelFeatures.RawData | ChannelFeatures.GroupDelivery);
}
