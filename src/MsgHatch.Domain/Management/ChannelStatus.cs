using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>The state of one registered channel in all tenants, because a channel is global (D106, D164).</summary>
/// <param name="Descriptor">The descriptor of the channel.</param>
/// <param name="Down">The down state, or null when the channel is up.</param>
/// <param name="Pending">The number of pending real messages.</param>
/// <param name="PendingTest">The number of pending test messages.</param>
/// <param name="OldestDueAtUtc">The earliest next try time of all pending messages, or null with no pending message.</param>
public sealed record ChannelStatus(
    ChannelDescriptor Descriptor, ChannelDown? Down, int Pending, int PendingTest, DateTime? OldestDueAtUtc);
