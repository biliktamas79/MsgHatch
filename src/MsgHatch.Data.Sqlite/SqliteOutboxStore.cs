using System.Text;
using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.Sqlite;

/// <summary>
/// The outbox in SQLite (section 4.5, D55): one row of the table <c>MsgHatchOutbox</c> for each outgoing message, with
/// the columns of its delivery state, its channel envelope and its route trace as JSON (D143), and its publish tries as
/// the JSON array <c>Tries</c> (D27). The table <c>MsgHatchChannelDown</c> has one row for each down channel (D106).
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

    private const string _downColumns = "Channel, Reason, SinceUtc, NextCheckAtUtc, ErrorCode, ErrorName, Note";

    private const string _downParameters = "@channel, @reason, @sinceUtc, @nextCheckAtUtc, @errorCode, @errorName, @note";

    private const string _downOfChannel = $"SELECT {_downColumns} FROM MsgHatchChannelDown WHERE Channel = @channel";

    private const string _downChannels = $"SELECT {_downColumns} FROM MsgHatchChannelDown";

    private const string _setDownOnce = $"INSERT OR IGNORE INTO MsgHatchChannelDown ({_downColumns}) VALUES ({_downParameters})";

    private const string _setDown = $"INSERT OR REPLACE INTO MsgHatchChannelDown ({_downColumns}) VALUES ({_downParameters})";

    private const string _setUp = "DELETE FROM MsgHatchChannelDown WHERE Channel = @channel AND (@reason IS NULL OR Reason = @reason)";

    private const string _startCheck = """
        UPDATE MsgHatchChannelDown SET NextCheckAtUtc = @nextCheckAtUtc
        WHERE Channel = @channel AND Reason = 'Errors' AND NextCheckAtUtc <= @nowUtc
        """;

    private const string _document = $$"""
        json_object(
            'schemaVersion', SchemaVersion, 'delivery', {{SqliteValues.DeliveryDocument}}, 'sourceMessageId', SourceMessageId,
            'tenant', NULLIF(TenantId, ''), 'type', MessageType, 'channel', Channel,
            'target', json_object('kind', TargetKind, 'id', TargetId), 'contentType', ContentType,
            'contentVersion', ContentVersion, 'flags', Flags, 'traceParent', TraceParent,
            'trace', IIF(json_valid(Trace), json(Trace), Trace), 'envelope', IIF(json_valid(Content), json(Content), Content))
        """;

    private static readonly ClaimStatements _claim = new(
        NextDue: "SELECT MIN(NextTryAtUtc) FROM MsgHatchOutbox WHERE Status = 'Pending' AND Channel = @channel",
        Lease: $$"""
            UPDATE MsgHatchOutbox SET NextTryAtUtc = @leaseUntilUtc, LeaseUntilUtc = @leaseUntilUtc
            WHERE rowid IN (
                SELECT rowid FROM MsgHatchOutbox WHERE Status = 'Pending' AND Channel = @channel AND NextTryAtUtc <= @nowUtc
                ORDER BY NextTryAtUtc, rowid LIMIT @maxCount)
            RETURNING rowid, {{_document}}
            """,
        Fail: "UPDATE MsgHatchOutbox SET Status = 'Failed', LeaseUntilUtc = NULL, LastError = @lastError WHERE rowid = @rowId");

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
                using var connection = SqliteSchema.Open(ConnectionString);
                return ErrorOr<ClaimResult<OutgoingMessage>>.ForValue(ReadDown(connection, channel) is { } down
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
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(() => ErrorOr<int>.ForValue(Execute(_deleteTestData, static command => command))));

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(() =>
        {
            using var connection = SqliteSchema.Open(ConnectionString);
            using var command = connection.Command(_downChannels);
            using var reader = command.ExecuteReader();
            var downs = new List<ChannelDown>();
            while (reader.Read())
            {
                downs.Add(DownOf(reader));
            }

            return ErrorOr<IReadOnlyList<ChannelDown>>.ForValue(downs);
        }));

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(down);
        return ValueTask.FromResult(SqliteFailures.Run(() => ErrorOr<bool>.ForValue(
            Execute(down.Reason == ChannelDownReason.Errors ? _setDownOnce : _setDown, command => command
                .With("@channel", down.Channel.Name)
                .With("@reason", down.Reason.ToString())
                .With("@sinceUtc", SqliteValues.Time(down.SinceUtc))
                .With("@nextCheckAtUtc", SqliteValues.Time(down.NextCheckAtUtc))
                .With("@errorCode", down.ErrorCode)
                .With("@errorName", down.ErrorName)
                .With("@note", down.Note)) > 0)));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(() => ErrorOr<bool>.ForValue(
            Execute(_setUp, command => command.With("@channel", channel.Name).With("@reason", reason?.ToString())) > 0)));

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
        var status = StatusAfter(publishTry.Outcome, nextTryAtUtc);
        using var connection = SqliteSchema.Open(ConnectionString);
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
            .With("@lastError", ErrorTextOf(publishTry));
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
            using var connection = SqliteSchema.Open(ConnectionString);
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
        using var connection = SqliteSchema.Open(ConnectionString);
        using var command = withValues(connection.Command(sql));
        return command.ExecuteNonQuery();
    }

    private static ChannelDown? ReadDown(SqliteConnection connection, ChannelId channel)
    {
        using var command = connection.Command(_downOfChannel).With("@channel", channel.Name);
        using var reader = command.ExecuteReader();
        return reader.Read() ? DownOf(reader) : null;
    }

    private static ChannelDown DownOf(SqliteDataReader row) =>
        new(
            new ChannelId(row.GetString(0)),
            Enum.Parse<ChannelDownReason>(row.GetString(1)),
            SqliteValues.ParseTime(row.GetString(2)),
            row.IsDBNull(3) ? null : SqliteValues.ParseTime(row.GetString(3)),
            row.IsDBNull(4) ? null : row.GetInt32(4),
            row.IsDBNull(5) ? null : row.GetString(5),
            row.IsDBNull(6) ? null : row.GetString(6));

    private static DeliveryStatus StatusAfter(PublishOutcome outcome, DateTime? nextTryAtUtc) => outcome switch
    {
        PublishOutcome.Succeeded or PublishOutcome.Suppressed => DeliveryStatus.Done,
        PublishOutcome.TransientError when nextTryAtUtc is not null => DeliveryStatus.Pending,
        _ => DeliveryStatus.Failed,
    };

    private static string? ErrorTextOf(PublishTry publishTry) =>
        publishTry.ErrorName is null ? null
        : publishTry.ErrorMessage is null ? publishTry.ErrorName
        : $"{publishTry.ErrorName}: {publishTry.ErrorMessage}";

    private sealed class TenantStore(SqliteOutboxStore store, TenantKey key) : IOutboxStore
    {
        public ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct) =>
            ValueTask.FromResult(store.Add(key, messages));
    }
}
