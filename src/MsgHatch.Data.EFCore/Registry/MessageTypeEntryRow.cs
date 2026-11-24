namespace MsgHatch.Data.EFCore.Registry;

/// <summary>
/// A row of the table <c>MsgHatchMessageTypeEntry</c>: the stored settings of one message type, with its disabled
/// channels as a JSON array of channel names (section 4.20).
/// </summary>
internal sealed class MessageTypeEntryRow
{
    public string Type { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public bool Enabled { get; set; }

    public string DisabledChannels { get; set; } = "[]";

    public int Revision { get; set; }

    public DateTime LastModifiedAtUtc { get; set; }
}
