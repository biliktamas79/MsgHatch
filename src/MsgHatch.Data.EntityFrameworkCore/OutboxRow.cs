namespace MsgHatch.Data.EntityFrameworkCore;

/// <summary>
/// A row of the table <c>MsgHatchOutbox</c>: one outgoing message, with its channel envelope as JSON in
/// <c>Content</c>, its route trace as JSON, and its publish tries as a JSON array (D27).
/// </summary>
internal sealed class OutboxRow : MessageRow
{
    public string SourceMessageId { get; set; } = string.Empty;

    public string MessageType { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;

    public string TargetKind { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    public string? TraceType { get; set; }

    public string? Trace { get; set; }

    public string Tries { get; set; } = "[]";
}
