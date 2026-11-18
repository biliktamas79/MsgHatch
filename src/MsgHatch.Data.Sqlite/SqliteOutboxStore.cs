using System.Text;
using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Data.Sqlite;

/// <summary>
/// The outbox in SQLite (section 4.5, D55): one row of the table <c>MsgHatchOutbox</c> for each outgoing message, with
/// the columns of its delivery state, its channel envelope and its route trace as JSON (D143), and its publish tries as
/// the JSON array <c>Tries</c> (D27). The table <c>MsgHatchChannelState</c> has the insert-only state rows of each channel,
/// with the primary key <c>(Channel, Version)</c> (D106, D277).
/// Each operation opens its own pooled connection. It is the dispatch store of all tenants, and <see cref="For"/> gives
/// the tenant store of one key.
/// </summary>
public sealed class SqliteOutboxStore : IOutboxDispatchStore
{
    private const string _insert = $$"""
        INSERT OR IGNORE INTO MsgHatchOutbox (
            {{SqliteValues.DeliveryColumns}}, TenantId, SchemaVersion, SourceMessageId, MessageType, Channel, TargetKind,
            TargetId, ContentType, ContentVersion, Flags, TraceParent, TraceType, Trace, Content)
        VALUES (
            {{SqliteValues.DeliveryParameters}}, @tenantId, @schemaVersion, @sourceMessageId, @messageType, @channel,
            @targetKind, @targetId, @contentType, @contentVersion, @flags, @traceParent,
            json_extract(@document, '$.trace."$type"'), json_extract(@document, '$.trace'), json_extract(@document, '$.envelope'))
        """;

    private const string _recordTry = """
        UPDATE MsgHatchOutbox SET
            Tries = json_insert(Tries, '$[#]', json_object(
                'tryNumber', @tryNumber, 'startedAtUtc', @startedAtUtc, 'finishedAtUtc', @finishedAtUtc, 'outcome', @outcome,
                'providerMessageId', @providerMessageId, 'errorCode', @errorCode, 'errorName', @errorName,
                'errorMessage', @errorMessage)),
            Status = @status, TryCount = @tryNumber, LastTryAtUtc = @finishedAtUtc,
            NextTryAtUtc = COALESCE(@nextTryAtUtc, NextTryAtUtc), LeaseUntilUtc = NULL, LastError = @lastError
        WHERE Id = @id AND TryCount = @tryNumber - 1
        """;

    private const string _isStored = "SELECT COUNT(*) FROM MsgHatchOutbox WHERE Id = @id";

    private const string _deleteTestData = "DELETE FROM MsgHatchOutbox WHERE Flags = 'Test'";

    private const string _stateColumns =
        "Channel, Version, Kind, SinceUtc, PreviousKind, PreviousDurationMs, Reason, NextCheckAtUtc, ErrorCode, ErrorName, Note";

    private const string _latestState = $"SELECT {_stateColumns} FROM MsgHatchChannelState WHERE Channel = @channel ORDER BY Version DESC LIMIT 1";

    // ponytail: the latest rows come from a scan of the primary key of all rows, add a table of the latest versions if a flapping channel adds very many rows
    private const string _downStates = $"""
        SELECT state.Channel, state.Version, state.Kind, state.SinceUtc, state.PreviousKind, state.PreviousDurationMs, state.Reason, state.NextCheckAtUtc,
            state.ErrorCode, state.ErrorName, state.Note
        FROM (SELECT Channel, MAX(Version) AS Version FROM MsgHatchChannelState GROUP BY Channel) AS latest
        JOIN MsgHatchChannelState AS state ON state.Channel = latest.Channel AND state.Version = latest.Version
        WHERE state.Kind = 'Down'
        """;

    private const string _addState = $"""
        INSERT OR IGNORE INTO MsgHatchChannelState ({_stateColumns})
        VALUES (@channel, @version, @kind, @sinceUtc, @previousKind, @previousDurationMs, @reason, @nextCheckAtUtc, @errorCode, @errorName, @note)
        """;

    private const string _statesFrom = $"""
        SELECT {_stateColumns} FROM MsgHatchChannelState AS state
        WHERE SinceUtc >= @fromUtc
            OR Version = (SELECT MAX(Version) FROM MsgHatchChannelState WHERE Channel = state.Channel AND SinceUtc < @fromUtc)
        ORDER BY SinceUtc DESC, Channel, Version DESC
        """;

    private const string _startCheck = """
        UPDATE MsgHatchChannelState SET NextCheckAtUtc = @nextCheckAtUtc
        WHERE Channel = @channel AND Kind = 'Down' AND Reason = 'Errors' AND NextCheckAtUtc <= @nowUtc
            AND Version = (SELECT MAX(Version) FROM MsgHatchChannelState WHERE Channel = @channel)
        """;

    private const string _document = $$"""
        json_object(
            'schemaVersion', SchemaVersion, 'delivery', {{SqliteValues.DeliveryDocument}}, 'sourceMessageId', SourceMessageId,
            'tenant', NULLIF(TenantId, ''), 'type', MessageType, 'channel', Channel,
            'target', json_object('kind', TargetKind, 'id', TargetId), 'contentType', ContentType,
            'contentVersion', ContentVersion, 'flags', Flags, 'traceParent', TraceParent,
            'trace', IIF(json_valid(Trace), json(Trace), Trace), 'envelope', IIF(json_valid(Content), json(Content), Content))
        """;

    private const string _get = $"SELECT {_document}, Tries FROM MsgHatchOutbox WHERE TenantId = @tenantId AND Id = @id";

    private static readonly string _query = SqliteManagement.Outbox.QueryOf(
        "SourceMessageId, TenantId, MessageType, Channel, TargetKind, TargetId, ContentType, ContentVersion, Flags",
        extraFilters: """
            AND (@channel IS NULL OR Channel = @channel)
            AND (@targetKind IS NULL OR (TargetKind = @targetKind AND TargetId = @targetId))
            AND (@sourceMessageId IS NULL OR SourceMessageId = @sourceMessageId)
            """);

    private static readonly ClaimStatements _claim = new(
        NextDue: "SELECT MIN(NextTryAtUtc) FROM MsgHatchOutbox WHERE Status = 'Pending' AND Channel = @channel",
        Due: """
            SELECT Sequence FROM MsgHatchOutbox WHERE Status = 'Pending' AND Channel = @channel AND NextTryAtUtc <= @nowUtc
            ORDER BY NextTryAtUtc, Sequence LIMIT @maxCount
            """,
        Lease: $$"""
            UPDATE MsgHatchOutbox SET NextTryAtUtc = @leaseUntilUtc, LeaseUntilUtc = @leaseUntilUtc
            WHERE Sequence IN (SELECT value FROM json_each(@sequences))
            RETURNING Sequence, {{_document}}
            """,
        Fail: "UPDATE MsgHatchOutbox SET Status = 'Failed', LeaseUntilUtc = NULL, LastError = @lastError WHERE Sequence = @sequence");

    private readonly MsgHatchJson _json;

    /// <summary>Makes the store on a database.</summary>
    /// <param name="connectionString">The connection string of the database, for example <c>Data Source=msghatch.db</c>.</param>
    /// <param name="json">The serializer of the messages.</param>
    /// <exception cref="ArgumentException">The connection string is not a valid SQLite connection string.</exception>
    public SqliteOutboxStore(string connectionString, MsgHatchJson json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(json);
        _ = new SqliteConnectionStringBuilder(connectionString);
        ConnectionString = connectionString;
        _json = json;
    }

    /// <summary>The connection string of the database.</summary>
    public string ConnectionString { get; }

    /// <summary>Gives the tenant store of one key. It reads and writes only the messages of its tenant.</summary>
    /// <param name="key">The tenant key.</param>
    /// <returns>The tenant store.</returns>
    public IOutboxStore For(TenantKey key) => new TenantStore(this, key);

    /// <inheritdoc />
    public ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimDueAsync(
        ChannelId channel, int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        ValueTask.FromResult(maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : SqliteFailures.Run(() =>
            {
                using var connection = SqliteSchema.Core.Open(ConnectionString);
                return ErrorOr<ClaimResult<OutgoingMessage>>.ForValue(ReadLatestState(connection, channel)?.ToDown() is { } down
                    ? new ClaimResult<OutgoingMessage>([], NextDueAtUtc: null, down)
                    : SqliteClaim.Run(
                        connection, _claim, channel.Name, maxCount, nowUtc, nowUtc + lease, document => _json.DeserializeOutgoing(document)));
            }));

    /// <inheritdoc />
    public ValueTask<Error?> RecordTryAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publishTry);
        return ValueTask.FromResult(SqliteFailures.Run(() => RecordTry(message, publishTry, nextTryAtUtc)));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ValueTask.FromResult(SqliteFailures.Run(() =>
            ErrorOr<IReadOnlyList<MessageCount>>.ForValue(SqliteManagement.Outbox.Count(ConnectionString, query, tenant: null))));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        ValueTask.FromResult(maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : SqliteFailures.Run(() => ErrorOr<int>.ForValue(SqliteManagement.Outbox.Purge(ConnectionString, finishedBeforeUtc, maxCount))));

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(() => ErrorOr<int>.ForValue(Execute(_deleteTestData, static command => command))));

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(
            () => ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([.. ReadStates(_downStates, static command => command).Select(static row => row.ToDown()!)])));

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(down);
        return ValueTask.FromResult(SqliteFailures.Run(
            () => ChannelState.Change(() => ReadLatestState(down.Channel), latest => ChannelState.AfterDown(latest, down), TryAddState)));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelStatesAsync(DateTime fromUtc, CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(() => ErrorOr<IReadOnlyList<ChannelState>>.ForValue(
            ReadStates(_statesFrom, command => command.With("@fromUtc", SqliteValues.Time(fromUtc))))));

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, DateTime upAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(() => ChannelState.Change(
            () => ReadLatestState(channel), latest => ChannelState.AfterUp(latest, channel, reason, upAtUtc), TryAddState)));

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(() => ErrorOr<bool>.ForValue(
            Execute(_startCheck, command => command
                .With("@channel", channel.Name)
                .With("@nowUtc", SqliteValues.Time(nowUtc))
                .With("@nextCheckAtUtc", SqliteValues.Time(nextCheckAtUtc))) > 0)));

    private Error? RecordTry(OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc)
    {
        var status = publishTry.StatusAfter(nextTryAtUtc);
        using var connection = SqliteSchema.Core.Open(ConnectionString);
        using var command = connection.Command(_recordTry)
            .With("@id", SqliteValues.Id(message.Delivery.Id))
            .With("@tryNumber", publishTry.TryNumber)
            .With("@startedAtUtc", SqliteValues.Time(publishTry.StartedAtUtc))
            .With("@finishedAtUtc", SqliteValues.Time(publishTry.FinishedAtUtc))
            .With("@outcome", publishTry.Outcome.ToString())
            .With("@providerMessageId", publishTry.ProviderMessageId)
            .With("@errorCode", publishTry.ErrorCode)
            .With("@errorName", publishTry.ErrorName)
            .With("@errorMessage", publishTry.ErrorMessage)
            .With("@status", status.ToString())
            .With("@nextTryAtUtc", status == DeliveryStatus.Pending ? SqliteValues.Time(nextTryAtUtc) : null)
            .With("@lastError", publishTry.ErrorText());
        if (command.ExecuteNonQuery() > 0)
        {
            return null;
        }

        using var isStored = connection.Command(_isStored).With("@id", SqliteValues.Id(message.Delivery.Id));
        return (long)isStored.ExecuteScalar()! == 0 ? new Error(Errors.NotFound, "The outgoing message is not stored.") : null;
    }

    private Error? Add(TenantKey key, IReadOnlyList<OutgoingMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Any(message => message.Tenant != key.Tenant))
        {
            return new Error(Errors.ValidationFailed, "A message has another tenant than the key of the store.");
        }

        var documents = new List<(OutgoingMessage Message, string Document)>(messages.Count);
        foreach (var message in messages)
        {
            var document = _json.Serialize(message);
            if (document.IsError)
            {
                return document.Error;
            }

            documents.Add((message, Encoding.UTF8.GetString(document.Value)));
        }

        return SqliteFailures.Run(() =>
        {
            using var connection = SqliteSchema.Core.Open(ConnectionString);
            using var transaction = connection.BeginTransaction(deferred: false);
            foreach (var (message, document) in documents)
            {
                using var command = connection.Command(_insert, transaction)
                    .WithDelivery(message.Delivery)
                    .With("@tenantId", SqliteValues.Tenant(message.Tenant))
                    .With("@schemaVersion", message.SchemaVersion)
                    .With("@sourceMessageId", SqliteValues.Id(message.SourceMessageId))
                    .With("@messageType", message.Type.Name)
                    .With("@channel", message.Channel.Name)
                    .With("@targetKind", message.Target.Kind.ToString())
                    .With("@targetId", message.Target.Id)
                    .With("@contentType", message.ContentType)
                    .With("@contentVersion", message.ContentVersion)
                    .With("@flags", message.Flags.ToString())
                    .With("@traceParent", message.TraceParent)
                    .With("@document", document);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return null;
        });
    }

    private int Execute(string sql, Func<SqliteCommand, SqliteCommand> withValues)
    {
        using var connection = SqliteSchema.Core.Open(ConnectionString);
        using var command = withValues(connection.Command(sql));
        return command.ExecuteNonQuery();
    }

    private ChannelState? ReadLatestState(ChannelId channel)
    {
        using var connection = SqliteSchema.Core.Open(ConnectionString);
        return ReadLatestState(connection, channel);
    }

    private static ChannelState? ReadLatestState(SqliteConnection connection, ChannelId channel)
    {
        using var command = connection.Command(_latestState).With("@channel", channel.Name);
        using var reader = command.ExecuteReader();
        return reader.Read() ? StateOf(reader) : null;
    }

    private List<ChannelState> ReadStates(string sql, Func<SqliteCommand, SqliteCommand> withValues)
    {
        using var connection = SqliteSchema.Core.Open(ConnectionString);
        using var command = withValues(connection.Command(sql));
        using var reader = command.ExecuteReader();
        var rows = new List<ChannelState>();
        while (reader.Read())
        {
            rows.Add(StateOf(reader));
        }

        return rows;
    }

    /// <summary>Inserts a state row. A stored version keeps its row, and gives false (D277).</summary>
    private bool TryAddState(ChannelState row) =>
        Execute(_addState, command => command
            .With("@channel", row.Channel.Name)
            .With("@version", row.Version)
            .With("@kind", row.Kind.ToString())
            .With("@sinceUtc", SqliteValues.Time(row.SinceUtc))
            .With("@previousKind", row.PreviousKind?.ToString())
            .With("@previousDurationMs", (long?)row.PreviousDuration?.TotalMilliseconds)
            .With("@reason", row.Reason?.ToString())
            .With("@nextCheckAtUtc", SqliteValues.Time(row.NextCheckAtUtc))
            .With("@errorCode", row.ErrorCode)
            .With("@errorName", row.ErrorName)
            .With("@note", row.Note)) > 0;

    private static ChannelState StateOf(SqliteDataReader row) =>
        new(
            new ChannelId(row.GetString(0)),
            row.GetInt32(1),
            Enum.Parse<ChannelStateKind>(row.GetString(2)),
            SqliteValues.ParseTime(row.GetString(3)),
            row.IsDBNull(4) ? null : Enum.Parse<ChannelStateKind>(row.GetString(4)),
            row.IsDBNull(5) ? null : TimeSpan.FromMilliseconds(row.GetInt64(5)),
            row.IsDBNull(6) ? null : Enum.Parse<ChannelDownReason>(row.GetString(6)),
            row.IsDBNull(7) ? null : SqliteValues.ParseTime(row.GetString(7)),
            row.IsDBNull(8) ? null : row.GetInt32(8),
            row.IsDBNull(9) ? null : row.GetString(9),
            row.IsDBNull(10) ? null : row.GetString(10));

    private MessagePage<OutgoingMessageSummary> Query(TenantKey key, OutboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var connection = SqliteSchema.Core.Open(ConnectionString);
        using var command = SqliteManagement.WithFilters(
                connection.Command(_query), key, query.Ids, query.Status, query.Type, query.CreatedFromUtc, query.CreatedToUtc,
                query.DueBeforeUtc, query.IncludeTest, query.After, query.Limit)
            .With("@channel", query.Channel?.Name)
            .With("@targetKind", query.Target?.Kind.ToString())
            .With("@targetId", query.Target?.Id)
            .With("@sourceMessageId", query.SourceMessageId is { } source ? SqliteValues.Id(source) : null);
        return SqliteManagement.Page(command, query.Limit, SummaryOf, static summary => summary.Delivery);
    }

    private ErrorOr<OutgoingMessageHistory?> Get(TenantKey key, Guid id)
    {
        using var connection = SqliteSchema.Core.Open(ConnectionString);
        using var command = connection.Command(_get).With("@tenantId", SqliteValues.Tenant(key.Tenant)).With("@id", SqliteValues.Id(id));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return ErrorOr<OutgoingMessageHistory?>.ForValue(null);
        }

        var message = _json.DeserializeOutgoing(Encoding.UTF8.GetBytes(reader.GetString(0)));
        var tries = MsgHatchJson.DeserializeTries(id, Encoding.UTF8.GetBytes(reader.GetString(1)));
        return message.Error ?? tries.Error ?? (ErrorOr<OutgoingMessageHistory?>)new OutgoingMessageHistory(message.Value, tries.Value);
    }

    private static OutgoingMessageSummary SummaryOf(SqliteDataReader row) =>
        new(
            SqliteValues.DeliveryOf(row), Guid.Parse(row.GetString(10)), SqliteValues.ParseTenant(row.GetString(11)),
            new MessageType(row.GetString(12)), new ChannelId(row.GetString(13)),
            new DeliveryTarget(Enum.Parse<DeliveryTargetKind>(row.GetString(14)), row.GetString(15)), row.GetString(16),
            row.GetInt32(17), Enum.Parse<MessageFlags>(row.GetString(18)));

    private sealed class TenantStore(SqliteOutboxStore store, TenantKey key) : IOutboxStore
    {
        public ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct) =>
            ValueTask.FromResult(store.Add(key, messages));

        public ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct) =>
            ValueTask.FromResult(SqliteFailures.Run(() => ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(store.Query(key, query))));

        public ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct) =>
            ValueTask.FromResult(SqliteFailures.Run(() => store.Get(key, id)));

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
            ValueTask.FromResult(SqliteFailures.Run(() =>
                ErrorOr<IReadOnlyList<MessageCount>>.ForValue(SqliteManagement.Outbox.Count(store.ConnectionString, query, key))));

        public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
            ValueTask.FromResult(SqliteFailures.Run(() =>
                ErrorOr<int>.ForValue(SqliteManagement.Outbox.Retry(store.ConnectionString, key, ids, nowUtc, addedTries))));

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            ValueTask.FromResult(SqliteFailures.Run(() =>
                ErrorOr<int>.ForValue(SqliteManagement.Outbox.Cancel(store.ConnectionString, key, ids, nowUtc))));
    }
}
