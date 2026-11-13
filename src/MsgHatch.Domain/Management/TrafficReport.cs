namespace MsgHatch.Domain.Management;

/// <summary>The traffic counts of the inbox and the outbox of one tenant (section 4.19).</summary>
/// <param name="Inbox">The counts of the received messages.</param>
/// <param name="Outbox">The counts of the outgoing messages.</param>
public sealed record TrafficReport(IReadOnlyList<MessageCount> Inbox, IReadOnlyList<MessageCount> Outbox);
