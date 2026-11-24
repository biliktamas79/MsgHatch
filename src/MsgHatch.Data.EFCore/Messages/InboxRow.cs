namespace MsgHatch.Data.EFCore.Messages;

/// <summary>A row of the table <c>Inbox</c>: one received message, with its event as JSON in <c>Content</c>.</summary>
internal sealed class InboxRow : MessageRow;
