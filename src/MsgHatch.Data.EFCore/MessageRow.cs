namespace MsgHatch.Data.EFCore;

/// <summary>
/// The columns that the two message tables share (section 4.5): the counter <see cref="Sequence"/> of the claim order
/// (D144), the tenant with an empty text for no tenant (D114), the id as text in the format <c>D</c>, the delivery
/// state, the token and the due time before the lease of the last claim (D318), the versions, the flags, and the content
/// as JSON. An enum value is text.
/// </summary>
internal abstract class MessageRow
{
    public long Sequence { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int TryCount { get; set; }

    public int MaxTryCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastTryAtUtc { get; set; }

    public DateTime NextTryAtUtc { get; set; }

    public DateTime? LeaseUntilUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public string? LastError { get; set; }

    public string? ClaimToken { get; set; }

    public DateTime? ClaimedDueAtUtc { get; set; }

    public int SchemaVersion { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public int ContentVersion { get; set; }

    public string Flags { get; set; } = string.Empty;

    public string? TraceParent { get; set; }

    public string Content { get; set; } = string.Empty;
}
