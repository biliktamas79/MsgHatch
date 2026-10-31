using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// Gives the registered channels and message types from a frozen snapshot (D117).
/// A read completes at once, does not allocate, and has no error result.
/// </summary>
public interface IMessageRegistry
{
    /// <summary>Each registered channel.</summary>
    IReadOnlyList<ChannelDescriptor> Channels { get; }

    /// <summary>Each registered message type.</summary>
    IReadOnlyList<MessageTypeDescriptor> MessageTypes { get; }

    /// <summary>Finds a channel.</summary>
    /// <param name="channel">The id of the channel.</param>
    /// <returns>The descriptor, or null for an unknown channel.</returns>
    ChannelDescriptor? FindChannel(ChannelId channel);

    /// <summary>Finds a message type.</summary>
    /// <param name="type">The message type.</param>
    /// <returns>The descriptor, or null for an unknown message type.</returns>
    MessageTypeDescriptor? FindMessageType(MessageType type);
}
