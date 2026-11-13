namespace MsgHatch.Data.EFCore;

/// <summary>A row of the table <c>MsgHatchChannelEntry</c>: the stored settings of one channel (section 4.20).</summary>
internal sealed class ChannelEntryRow
{
    public string Channel { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public bool Enabled { get; set; }

    public int Revision { get; set; }

    public DateTime LastModifiedAtUtc { get; set; }
}
