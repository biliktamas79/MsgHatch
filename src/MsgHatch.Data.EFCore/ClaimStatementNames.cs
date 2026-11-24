namespace MsgHatch.Data.EFCore;

/// <summary>
/// The names of one claim statement of a message table, each quoted for the provider: the table with its schema, the
/// columns, and the placeholders of the parameters. The statement claims at most <see cref="MaxCountParameter"/>
/// pending rows that are due at <see cref="NowUtcParameter"/>, in the order of <see cref="NextTryAtUtc"/> and then
/// <see cref="Sequence"/> (D144). It sets <see cref="ClaimedDueAtUtc"/> to the due time before the claim, then
/// <see cref="ClaimToken"/>, <see cref="NextTryAtUtc"/>, and <see cref="LeaseUntilUtc"/>, and returns each column of each
/// claimed row with its values after the claim.
/// </summary>
internal sealed record ClaimStatementNames
{
    /// <summary>The table with its schema.</summary>
    public required string Table { get; init; }

    public required string Sequence { get; init; }

    public required string Status { get; init; }

    public required string NextTryAtUtc { get; init; }

    public required string LeaseUntilUtc { get; init; }

    public required string ClaimToken { get; init; }

    public required string ClaimedDueAtUtc { get; init; }

    /// <summary>The channel column of the outbox, or null for the inbox, which claims the rows of each channel.</summary>
    public string? Channel { get; init; }

    public required string MaxCountParameter { get; init; }

    public required string NowUtcParameter { get; init; }

    public required string LeaseUntilUtcParameter { get; init; }

    public required string ClaimTokenParameter { get; init; }

    /// <summary>The status <c>Pending</c> as a parameter.</summary>
    public required string PendingParameter { get; init; }

    /// <summary>The channel of the outbox claim, or null for the inbox.</summary>
    public string? ChannelParameter { get; init; }
}
