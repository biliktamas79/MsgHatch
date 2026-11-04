using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions;

/// <summary>The registered channels of a message type, with their features, from the registry.</summary>
internal static class MessageChannels
{
    public static List<ChannelDescriptor> Of(IMessageRegistry registry, MessageType type)
    {
        var channels = new List<ChannelDescriptor>();
        foreach (var id in registry.FindMessageType(type)?.Channels ?? [])
        {
            if (registry.FindChannel(id) is { } channel)
            {
                channels.Add(channel);
            }
        }

        return channels;
    }
}
