using System.Collections.Frozen;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>The registry reader from the registrations, as a frozen snapshot with no database (D117).</summary>
internal sealed class InMemoryMessageRegistry : IMessageRegistry
{
    private readonly FrozenDictionary<ChannelId, ChannelDescriptor> _channels;
    private readonly FrozenDictionary<MessageType, MessageTypeDescriptor> _messageTypes;

    public InMemoryMessageRegistry(IEnumerable<ChannelDescriptor> channels, IEnumerable<MessageTypeDescriptor> messageTypes)
    {
        Channels = [.. channels];
        MessageTypes = [.. messageTypes];
        _channels = Channels.DistinctBy(static channel => channel.Id).ToFrozenDictionary(static channel => channel.Id);
        _messageTypes = MessageTypes.DistinctBy(static type => type.Type).ToFrozenDictionary(static type => type.Type);
    }

    public IReadOnlyList<ChannelDescriptor> Channels { get; }

    public IReadOnlyList<MessageTypeDescriptor> MessageTypes { get; }

    public ChannelDescriptor? FindChannel(ChannelId channel) => _channels.GetValueOrDefault(channel);

    public MessageTypeDescriptor? FindMessageType(MessageType type) => _messageTypes.GetValueOrDefault(type);
}
