namespace MsgHatch.Data.EFCore;

/// <summary>A row of the table <c>MsgHatchChannelState</c>: one insert-only state of a channel (D106, D277).</summary>
internal sealed class ChannelStateRow
{
    public string Channel { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Kind { get; set; } = string.Empty;

    public DateTime SinceUtc { get; set; }

    public string? PreviousKind { get; set; }

    public long? PreviousDurationMs { get; set; }

    public string? Reason { get; set; }

    public DateTime? NextCheckAtUtc { get; set; }

    public int? ErrorCode { get; set; }

    public string? ErrorName { get; set; }

    public string? Note { get; set; }
}
