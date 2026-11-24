namespace MsgHatch.Data.EFCore;

/// <summary>
/// The names of one claim statement of a message table for <see cref="IEntityFrameworkStoreDialect.ClaimStatement"/>
/// (D318), each quoted for the provider with the names of the EF Core model and its <c>ISqlGenerationHelper</c>: the table
/// with its schema, the columns, and the placeholders of the parameters. The statement claims at most
/// <see cref="MaxCountParameter"/> rows with the status <see cref="PendingParameter"/> that are due at
/// <see cref="NowUtcParameter"/>, in the order of <see cref="NextTryAtUtc"/> and then <see cref="Sequence"/> (D144). It
/// sets <see cref="ClaimedDueAtUtc"/> to the due time before the claim, then <see cref="ClaimToken"/> to
/// <see cref="ClaimTokenParameter"/>, and <see cref="NextTryAtUtc"/> and <see cref="LeaseUntilUtc"/> to
/// <see cref="LeaseUntilUtcParameter"/>. It returns each column of each claimed row with its values after the claim.
/// Two parallel claims must claim different rows.
/// </summary>
public sealed record ClaimStatementNames
{
    /// <summary>The table with its schema.</summary>
    public required string Table { get; init; }

    /// <summary>The counter of the add order, the key of a message table.</summary>
    public required string Sequence { get; init; }

    /// <summary>The status column.</summary>
    public required string Status { get; init; }

    /// <summary>The column of the due time.</summary>
    public required string NextTryAtUtc { get; init; }

    /// <summary>The column of the end of the lease.</summary>
    public required string LeaseUntilUtc { get; init; }

    /// <summary>The column of the token of the last claim.</summary>
    public required string ClaimToken { get; init; }

    /// <summary>The column of the due time before the last claim, which gives the order of the claimed rows.</summary>
    public required string ClaimedDueAtUtc { get; init; }

    /// <summary>The channel column of the outbox, or null for the inbox, which claims the rows of each channel.</summary>
    public string? Channel { get; init; }

    /// <summary>The maximum count of claimed rows.</summary>
    public required string MaxCountParameter { get; init; }

    /// <summary>The time of the claim.</summary>
    public required string NowUtcParameter { get; init; }

    /// <summary>The end of the lease, which is the next due time of a claimed row (D103).</summary>
    public required string LeaseUntilUtcParameter { get; init; }

    /// <summary>The new claim token.</summary>
    public required string ClaimTokenParameter { get; init; }

    /// <summary>The status <c>Pending</c> as a parameter.</summary>
    public required string PendingParameter { get; init; }

    /// <summary>The channel of the outbox claim, or null for the inbox.</summary>
    public string? ChannelParameter { get; init; }
}
