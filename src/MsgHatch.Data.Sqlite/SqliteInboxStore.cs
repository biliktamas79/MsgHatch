using System.Text;
using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.Sqlite;

/// <summary>
/// The inbox in SQLite (section 4.5, D55): one row of the table <c>MsgHatchInbox</c> for each received message, with
/// the columns of its delivery state, and its event as JSON in the column <c>Content</c> (D143). The key of a row is
/// the tenant and the id. Each operation opens its own pooled connection. It is the dispatch store of all tenants, and
/// <see cref="For"/> gives the tenant store of one key.
/// </summary>
public sealed class SqliteInboxStore : IInboxDispatchStore
{
    private const string _insert = $$"""
        INSERT OR IGNORE INTO MsgHatchInbox (
            TenantId, {{SqliteValues.DeliveryColumns}}, SchemaVersion, ContentType, ContentVersion, Flags, TraceParent, Content)
        VALUES (
            @tenantId, {{SqliteValues.DeliveryParameters}}, @schemaVersion, @contentType, @contentVersion, @flags, @traceParent,
            json_extract(@document, '$.event'))
        """;

    private const string _complete = """
        UPDATE MsgHatchInbox SET Status = 'Done', TryCount = TryCount + 1, LastTryAtUtc = @triedAtUtc, LeaseUntilUtc = NULL
        WHERE TenantId = @tenantId AND Id = @id
        """;

    private const string _fail = """
        UPDATE MsgHatchInbox SET
            Status = @status, TryCount = TryCount + 1, LastTryAtUtc = @triedAtUtc,
            NextTryAtUtc = COALESCE(@nextTryAtUtc, NextTryAtUtc), LeaseUntilUtc = NULL, LastError = @lastError
        WHERE TenantId = @tenantId AND Id = @id
        """;

    private const string _deleteTestData = "DELETE FROM MsgHatchInbox WHERE Flags = 'Test'";

    private const string _document = $$"""
        json_object(
            'schemaVersion', SchemaVersion, 'delivery', {{SqliteValues.DeliveryDocument}}, 'tenant', NULLIF(TenantId, ''),
            'type', ContentType, 'contentVersion', ContentVersion, 'flags', Flags, 'traceParent', TraceParent,
            'event', IIF(json_valid(Content), json(Content), Content))
        """;

    private const string _get = $"SELECT {_document} FROM MsgHatchInbox WHERE TenantId = @tenantId AND Id = @id";

    private static readonly string _query = SqliteManagement.Inbox.QueryOf("TenantId, ContentType, ContentVersion, Flags", extraFilters: string.Empty);

    private static readonly ClaimStatements _claim = new(
        NextDue: "SELECT MIN(NextTryAtUtc) FROM MsgHatchInbox WHERE Status = 'Pending'",
        Due: """
            SELECT Sequence FROM MsgHatchInbox WHERE Status = 'Pending' AND NextTryAtUtc <= @nowUtc
            ORDER BY NextTryAtUtc, Sequence LIMIT @maxCount
            """,
        Lease: $$"""
            UPDATE MsgHatchInbox SET NextTryAtUtc = @leaseUntilUtc, LeaseUntilUtc = @leaseUntilUtc
            WHERE Sequence IN (SELECT value FROM json_each(@sequences))
            RETURNING Sequence, {{_document}}
            """,
        Fail: "UPDATE MsgHatchInbox SET Status = 'Failed', LeaseUntilUtc = NULL, LastError = @lastError WHERE Sequence = @sequence");

    private readonly MsgHatchJson _json;

    /// <summary>Makes the store on a database.</summary>
    /// <param name="connectionString">The connection string of the database, for example <c>Data Source=msghatch.db</c>.</param>
    /// <param name="json">The serializer of the messages.</param>
    /// <exception cref="ArgumentException">The connection string is not a valid SQLite connection string.</exception>
    public SqliteInboxStore(string connectionString, MsgHatchJson json)
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
    public IInboxStore For(TenantKey key) => new TenantStore(this, key);

    /// <inheritdoc />
    public ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimDueAsync(
        int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        ValueTask.FromResult(maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : SqliteFailures.Run(() =>
            {
                using var connection = SqliteSchema.Core.Open(ConnectionString);
                return ErrorOr<ClaimResult<ReceivedMessage>>.ForValue(SqliteClaim.Run(
                    connection, _claim, channel: null, maxCount, nowUtc, nowUtc + lease, document => _json.DeserializeReceived(document)));
            }));

    /// <inheritdoc />
    public ValueTask<Error?> CompleteAsync(ReceivedMessage message, DateTime triedAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(Update(message, _complete, command => command.With("@triedAtUtc", SqliteValues.Time(triedAtUtc))));

    /// <inheritdoc />
    public ValueTask<Error?> FailAsync(
        ReceivedMessage message, string error, DateTime triedAtUtc, DateTime? nextTryAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(Update(message, _fail, command => command
            .With("@status", (nextTryAtUtc is null ? DeliveryStatus.Failed : DeliveryStatus.Pending).ToString())
            .With("@triedAtUtc", SqliteValues.Time(triedAtUtc))
            .With("@nextTryAtUtc", SqliteValues.Time(nextTryAtUtc))
            .With("@lastError", error)));

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        ValueTask.FromResult(maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : SqliteFailures.Run(() => ErrorOr<int>.ForValue(SqliteManagement.Inbox.Purge(ConnectionString, finishedBeforeUtc, maxCount))));

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(() =>
        {
            using var connection = SqliteSchema.Core.Open(ConnectionString);
            using var command = connection.Command(_deleteTestData);
            return ErrorOr<int>.ForValue(command.ExecuteNonQuery());
        }));

    private Error? Update(ReceivedMessage message, string sql, Func<SqliteCommand, SqliteCommand> withValues)
    {
        ArgumentNullException.ThrowIfNull(message);
        return SqliteFailures.Run(() =>
        {
            using var connection = SqliteSchema.Core.Open(ConnectionString);
            using var command = withValues(connection.Command(sql)
                .With("@tenantId", SqliteValues.Tenant(message.Tenant))
                .With("@id", SqliteValues.Id(message.Delivery.Id)));
            return command.ExecuteNonQuery() == 0 ? new Error(Errors.NotFound, "The received message is not stored.") : null;
        });
    }

    private Error? Add(TenantKey key, ReceivedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Tenant != key.Tenant)
        {
            return new Error(Errors.ValidationFailed, "The message has another tenant than the key of the store.");
        }

        var document = _json.Serialize(message);
        if (document.IsError)
        {
            return document.Error;
        }

        return SqliteFailures.Run(() =>
        {
            using var connection = SqliteSchema.Core.Open(ConnectionString);
            using var command = connection.Command(_insert)
                .With("@tenantId", SqliteValues.Tenant(message.Tenant))
                .WithDelivery(message.Delivery)
                .With("@schemaVersion", message.SchemaVersion)
                .With("@contentType", message.Type.Name)
                .With("@contentVersion", message.ContentVersion)
                .With("@flags", message.Flags.ToString())
                .With("@traceParent", message.TraceParent)
                .With("@document", Encoding.UTF8.GetString(document.Value));
            command.ExecuteNonQuery();
            return null;
        });
    }

    private MessagePage<ReceivedMessageSummary> Query(TenantKey key, InboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var connection = SqliteSchema.Core.Open(ConnectionString);
        using var command = SqliteManagement.WithFilters(
            connection.Command(_query), key, query.Ids, query.Status, query.Type, query.CreatedFromUtc, query.CreatedToUtc,
            query.DueBeforeUtc, query.IncludeTest, query.After, query.Limit);
        return SqliteManagement.Page(command, query.Limit, SummaryOf, static summary => summary.Delivery);
    }

    private ErrorOr<ReceivedMessage?> Get(TenantKey key, Guid id)
    {
        using var connection = SqliteSchema.Core.Open(ConnectionString);
        using var command = connection.Command(_get).With("@tenantId", SqliteValues.Tenant(key.Tenant)).With("@id", SqliteValues.Id(id));
        return command.ExecuteScalar() is string document
            ? _json.DeserializeReceived(Encoding.UTF8.GetBytes(document)).ThenIfSuccess(static message => ErrorOr<ReceivedMessage?>.ForValue(message))
            : ErrorOr<ReceivedMessage?>.ForValue(null);
    }

    private static ReceivedMessageSummary SummaryOf(SqliteDataReader row) =>
        new(
            SqliteValues.DeliveryOf(row), SqliteValues.ParseTenant(row.GetString(10)), new MessageType(row.GetString(11)),
            row.GetInt32(12), Enum.Parse<MessageFlags>(row.GetString(13)));

    private sealed class TenantStore(SqliteInboxStore store, TenantKey key) : IInboxStore
    {
        public ValueTask<Error?> AddAsync(ReceivedMessage message, CancellationToken ct) =>
            ValueTask.FromResult(store.Add(key, message));

        public ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> QueryAsync(InboxQuery query, CancellationToken ct) =>
            ValueTask.FromResult(SqliteFailures.Run(() => ErrorOr<MessagePage<ReceivedMessageSummary>>.ForValue(store.Query(key, query))));

        public ValueTask<ErrorOr<ReceivedMessage?>> GetAsync(Guid id, CancellationToken ct) =>
            ValueTask.FromResult(SqliteFailures.Run(() => store.Get(key, id)));

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
            ValueTask.FromResult(SqliteFailures.Run(() =>
                ErrorOr<IReadOnlyList<MessageCount>>.ForValue(SqliteManagement.Inbox.Count(store.ConnectionString, query, key))));

        public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
            ValueTask.FromResult(SqliteFailures.Run(() =>
                ErrorOr<int>.ForValue(SqliteManagement.Inbox.Retry(store.ConnectionString, key, ids, nowUtc, addedTries))));

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            ValueTask.FromResult(SqliteFailures.Run(() =>
                ErrorOr<int>.ForValue(SqliteManagement.Inbox.Cancel(store.ConnectionString, key, ids, nowUtc))));
    }
}
