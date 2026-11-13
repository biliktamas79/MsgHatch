namespace MsgHatch.Domain.Management;

/// <summary>The numbers of the messages that a test data delete or a purge deleted (section 4.19).</summary>
/// <param name="InboxMessages">The number of deleted received messages.</param>
/// <param name="OutboxMessages">The number of deleted outgoing messages.</param>
public readonly record struct DeletedMessages(int InboxMessages, int OutboxMessages);
