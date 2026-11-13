using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Management;

/// <summary>The number of the messages of one message type, one channel, one status, and one test flag (D164).</summary>
/// <param name="Type">The message type.</param>
/// <param name="Channel">The channel, or null for the inbox.</param>
/// <param name="Status">The status.</param>
/// <param name="Test">True for the messages with <see cref="MessageFlags.Test"/>.</param>
/// <param name="Count">The number of messages.</param>
/// <param name="OldestDueAtUtc">
/// The earliest next try time of the counted messages, only for the status <see cref="DeliveryStatus.Pending"/>.
/// </param>
public readonly record struct MessageCount(
    MessageType Type, ChannelId? Channel, DeliveryStatus Status, bool Test, int Count, DateTime? OldestDueAtUtc)
{
    /// <summary>
    /// Counts the messages for a store that reads them into memory: one row for each message type, channel, status, and
    /// test flag.
    /// </summary>
    /// <param name="messages">The message type, the channel, the delivery state, and the flags of each counted message.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<MessageCount> Of(
        IEnumerable<(MessageType Type, ChannelId? Channel, DeliveryState Delivery, MessageFlags Flags)> messages) =>
    [
        .. messages
            .GroupBy(static message => (message.Type, message.Channel, message.Delivery.Status, Test: message.Flags.HasFlag(MessageFlags.Test)))
            .Select(static group => new MessageCount(
                group.Key.Type, group.Key.Channel, group.Key.Status, group.Key.Test, group.Count(),
                group.Key.Status == DeliveryStatus.Pending ? group.Min(static message => message.Delivery.NextTryAtUtc) : null)),
    ];
}
