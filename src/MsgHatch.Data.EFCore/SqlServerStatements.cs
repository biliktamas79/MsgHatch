namespace MsgHatch.Data.EFCore;

/// <summary>
/// The raw SQL statements of the EF Core stores for SQL Server (D120): the statements that LINQ cannot express. A new
/// row is an <c>INSERT ... WHERE NOT EXISTS</c> with the hints <c>UPDLOCK, HOLDLOCK</c>, so a second add of a key
/// changes nothing, also from a parallel caller, and SQL Server takes the parts of the document into their columns with
/// <c>JSON_QUERY</c> and <c>JSON_VALUE</c> (D143). The claim is one <c>UPDATE</c> with an <c>OUTPUT</c> clause of the
/// rows of a <c>TOP</c> subquery with <c>ROWLOCK, READPAST, UPDLOCK</c> in the claim order (D144). An <c>UPDATE</c> of a
/// CTE with the <c>TOP</c> puts a sort for the Halloween protection under the <c>TOP</c>, so it locks each due row, and a
/// parallel claim gets fewer rows (checked fact of phase 18). A try is appended to the JSON array <c>Tries</c> with
/// <c>JSON_MODIFY</c> (D27). Each statement uses named parameters, and the schema of the model.
/// </summary>
/// <param name="schema">The schema of the tables, which <see cref="EntityFrameworkStoreOptions"/> checks.</param>
internal sealed class SqlServerStatements(string schema)
{
    private const string _deliveryColumns =
        "TenantId, Id, Status, TryCount, MaxTryCount, CreatedAtUtc, LastTryAtUtc, NextTryAtUtc, LeaseUntilUtc, ExpiresAtUtc, LastError";

    private const string _deliveryParameters =
        "@tenantId, @id, @status, @tryCount, @maxTryCount, @createdAtUtc, @lastTryAtUtc, @nextTryAtUtc, @leaseUntilUtc, @expiresAtUtc, @lastError";

    /// <summary>The columns of a claimed row, with the due time before the lease as <c>NextTryAtUtc</c>, so the store sorts the rows by it.</summary>
    private const string _claimedDeliveryColumns = """
        inserted.Sequence, inserted.TenantId, inserted.Id, inserted.Status, inserted.TryCount, inserted.MaxTryCount,
        inserted.CreatedAtUtc, inserted.LastTryAtUtc, deleted.NextTryAtUtc AS NextTryAtUtc, inserted.LeaseUntilUtc,
        inserted.ExpiresAtUtc, inserted.LastError, inserted.SchemaVersion, inserted.ContentType, inserted.ContentVersion,
        inserted.Flags, inserted.TraceParent, inserted.Content
        """;

    private readonly string _inbox = $"[{schema}].[MsgHatchInbox]";

    private readonly string _outbox = $"[{schema}].[MsgHatchOutbox]";

    private readonly string _channelState = $"[{schema}].[MsgHatchChannelState]";

    public string InsertReceived => $"""
        INSERT INTO {_inbox} (
            {_deliveryColumns}, SchemaVersion, ContentType, ContentVersion, Flags, TraceParent, Content)
        SELECT
            {_deliveryParameters}, @schemaVersion, @contentType, @contentVersion, @flags, @traceParent,
            JSON_QUERY(@document, '$.event')
        WHERE NOT EXISTS (SELECT 1 FROM {_inbox} WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @tenantId AND Id = @id)
        """;

    public string ClaimReceived => $"""
        UPDATE {_inbox} SET NextTryAtUtc = @leaseUntilUtc, LeaseUntilUtc = @leaseUntilUtc
        OUTPUT {_claimedDeliveryColumns}
        WHERE Sequence IN (
            SELECT TOP (@maxCount) due.Sequence FROM {_inbox} AS due WITH (ROWLOCK, READPAST, UPDLOCK)
            WHERE due.Status = 'Pending' AND due.NextTryAtUtc <= @nowUtc
            ORDER BY due.NextTryAtUtc, due.Sequence)
        """;

    public string InsertOutgoing => $"""
        INSERT INTO {_outbox} (
            {_deliveryColumns}, SchemaVersion, SourceMessageId, MessageType, Channel, TargetKind, TargetId, ContentType,
            ContentVersion, Flags, TraceParent, TraceType, Trace, Content, Tries)
        SELECT
            {_deliveryParameters}, @schemaVersion, @sourceMessageId, @messageType, @channel, @targetKind, @targetId,
            @contentType, @contentVersion, @flags, @traceParent, JSON_VALUE(@document, '$.trace."$type"'),
            JSON_QUERY(@document, '$.trace'), JSON_QUERY(@document, '$.envelope'), '[]'
        WHERE NOT EXISTS (SELECT 1 FROM {_outbox} WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id)
        """;

    public string ClaimOutgoing => $"""
        UPDATE {_outbox} SET NextTryAtUtc = @leaseUntilUtc, LeaseUntilUtc = @leaseUntilUtc
        OUTPUT {_claimedDeliveryColumns}, inserted.SourceMessageId, inserted.MessageType, inserted.Channel,
            inserted.TargetKind, inserted.TargetId, inserted.TraceType, inserted.Trace, inserted.Tries
        WHERE Sequence IN (
            SELECT TOP (@maxCount) due.Sequence FROM {_outbox} AS due WITH (ROWLOCK, READPAST, UPDLOCK)
            WHERE due.Status = 'Pending' AND due.Channel = @channel AND due.NextTryAtUtc <= @nowUtc
            ORDER BY due.NextTryAtUtc, due.Sequence)
        """;

    /// <summary>Appends the try <c>@try</c> with the number <c>@tryNumber</c>, only when the stored <c>TryCount</c> is the number before it.</summary>
    public string RecordTry => $"""
        UPDATE {_outbox} SET
            Tries = JSON_MODIFY(Tries, 'append $', JSON_QUERY(@try)),
            Status = @status, TryCount = @tryNumber, LastTryAtUtc = @finishedAtUtc,
            NextTryAtUtc = COALESCE(@nextTryAtUtc, NextTryAtUtc), LeaseUntilUtc = NULL, LastError = @lastError
        WHERE Id = @id AND TryCount = @tryNumber - 1
        """;

    /// <summary>Cancels the pending rows of the ids, and appends the cancelled try <c>@try</c> with the next try number of each row (D110).</summary>
    public string CancelOutgoing => $"""
        UPDATE {_outbox} SET
            Status = 'Cancelled', LeaseUntilUtc = NULL, TryCount = TryCount + 1, LastTryAtUtc = @nowUtc,
            Tries = JSON_MODIFY(Tries, 'append $', JSON_QUERY(JSON_MODIFY(@try, '$.tryNumber', TryCount + 1)))
        WHERE TenantId = @tenantId AND Id IN (SELECT value FROM OPENJSON(@ids)) AND Status = 'Pending'
            AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc <= @nowUtc)
        """;

    /// <summary>
    /// Adds a state row only when the channel has no row of its version, so of two parallel changes only the first adds its
    /// row (D277). A changed row count of 0 means that another caller added the version first.
    /// </summary>
    public string AddChannelState => $"""
        INSERT INTO {_channelState} (
            Channel, Version, Kind, SinceUtc, PreviousKind, PreviousDurationMs, Reason, NextCheckAtUtc, ErrorCode, ErrorName, Note)
        SELECT @channel, @version, @kind, @sinceUtc, @previousKind, @previousDurationMs, @reason, @nextCheckAtUtc, @errorCode, @errorName, @note
        WHERE NOT EXISTS (SELECT 1 FROM {_channelState} WITH (UPDLOCK, HOLDLOCK) WHERE Channel = @channel AND Version = @version)
        """;
}
