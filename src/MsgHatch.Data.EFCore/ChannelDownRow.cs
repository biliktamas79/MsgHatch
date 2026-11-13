namespace MsgHatch.Data.EFCore;

/// <summary>A row of the table <c>MsgHatchChannelDown</c>: the down state of one channel (D106).</summary>
internal sealed class ChannelDownRow
{
    public string Channel { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTime SinceUtc { get; set; }

    public DateTime? NextCheckAtUtc { get; set; }

    public int? ErrorCode { get; set; }

    public string? ErrorName { get; set; }

    public string? Note { get; set; }
}
