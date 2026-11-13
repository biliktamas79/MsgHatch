using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Data.Sqlite;

/// <summary>
/// The statements of the delivery management that the inbox and the outbox share (section 4.19): the page of a query,
/// the count of the traffic with one row for each message type, channel, status, and test flag (D164), the retry
/// (D111), the cancel (D110), and the purge of the finished messages (D128). A null tenant parameter counts all tenants.
/// </summary>
/// <param name="table">The message table.</param>
/// <param name="typeColumn">The column of the message type.</param>
/// <param name="channelColumn">The channel column, or <c>NULL</c> for the inbox.</param>
/// <param name="cancelSet">The extra assignments of a cancel, for example the cancelled try of the outbox.</param>
internal sealed class SqliteManagement(string table, string typeColumn, string channelColumn, string cancelSet)
{
    public static readonly SqliteManagement Inbox = new("MsgHatchInbox", "ContentType", "NULL", cancelSet: string.Empty);

    public static readonly SqliteManagement Outbox = new("MsgHatchOutbox", "MessageType", "Channel", """
        , TryCount = TryCount + 1, LastTryAtUtc = @nowUtc,
        Tries = json_insert(Tries, '$[#]', json_object(
            'tryNumber', TryCount + 1, 'startedAtUtc', @nowUtc, 'finishedAtUtc', @nowUtc, 'outcome', 'Cancelled',
            'providerMessageId', NULL, 'errorCode', NULL, 'errorName', NULL, 'errorMessage', NULL))
        """);

    private const string _ofIds = "TenantId = @tenantId AND Id IN (SELECT value FROM json_each(@ids))";

    private const string _noActiveLease = "(LeaseUntilUtc IS NULL OR LeaseUntilUtc <= @nowUtc)";

    private readonly string _purge = $"""
        DELETE FROM {table} WHERE Sequence IN (
            SELECT Sequence FROM {table}
            WHERE Status IN ('Done', 'Failed', 'Cancelled') AND COALESCE(LastTryAtUtc, CreatedAtUtc) < @finishedBeforeUtc
            ORDER BY COALESCE(LastTryAtUtc, CreatedAtUtc) LIMIT @maxCount)
        """;

    private readonly string _count = $"""
        SELECT {typeColumn}, {channelColumn}, Status, Flags = 'Test', COUNT(*), MIN(IIF(Status = 'Pending', NextTryAtUtc, NULL))
        FROM {table}
        WHERE (@tenantId IS NULL OR TenantId = @tenantId)
            AND (@createdFromUtc IS NULL OR CreatedAtUtc >= @createdFromUtc) AND (@createdToUtc IS NULL OR CreatedAtUtc < @createdToUtc)
            AND (@status IS NULL OR Status = @status) AND (@includeTest OR Flags IS NOT 'Test')
        GROUP BY {typeColumn}, {channelColumn}, Status, Flags = 'Test'
        """;

    private readonly string _retry = $"""
        UPDATE {table} SET
            Status = 'Pending', NextTryAtUtc = @nowUtc, LeaseUntilUtc = NULL,
            MaxTryCount = IIF(Status = 'Failed', TryCount + @addedTries, MaxTryCount)
        WHERE {_ofIds} AND Status IN ('Failed', 'Pending') AND {_noActiveLease}
            AND (ExpiresAtUtc IS NULL OR ExpiresAtUtc > @nowUtc)
        """;

    private readonly string _cancel = $"""
        UPDATE {table} SET Status = 'Cancelled', LeaseUntilUtc = NULL {cancelSet}
        WHERE {_ofIds} AND Status = 'Pending' AND {_noActiveLease}
        """;

    /// <summary>The filters of a query that the inbox and the outbox share, with the cursor of the page (D112).</summary>
    private readonly string _filters = $"""
        TenantId = @tenantId
            AND (@ids IS NULL OR Id IN (SELECT value FROM json_each(@ids))) AND (@status IS NULL OR Status = @status)
            AND (@messageType IS NULL OR {typeColumn} = @messageType)
            AND (@createdFromUtc IS NULL OR CreatedAtUtc >= @createdFromUtc) AND (@createdToUtc IS NULL OR CreatedAtUtc < @createdToUtc)
            AND (@dueBeforeUtc IS NULL OR (Status = 'Pending' AND NextTryAtUtc < @dueBeforeUtc))
            AND (@includeTest OR Flags IS NOT 'Test')
            AND (@afterCreatedAtUtc IS NULL OR CreatedAtUtc < @afterCreatedAtUtc OR (CreatedAtUtc = @afterCreatedAtUtc AND Id < @afterId))
        """;

    /// <summary>Gives the statement of a page: the delivery columns, the summary columns, the filters, and the order newest first.</summary>
    public string QueryOf(string summaryColumns, string extraFilters) => $"""
        SELECT {SqliteValues.DeliveryColumns}, {summaryColumns} FROM {table}
        WHERE {_filters} {extraFilters}
        ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @limit
        """;

    /// <summary>Reads a page of summaries. The command has the parameters of <see cref="WithFilters"/>.</summary>
    public static MessagePage<T> Page<T>(SqliteCommand command, int limit, Func<SqliteDataReader, T> summary, Func<T, DeliveryState> delivery)
    {
        using var reader = command.ExecuteReader();
        var items = new List<T>();
        while (reader.Read())
        {
            items.Add(summary(reader));
        }

        return new MessagePage<T>(items, items.Count > 0 && items.Count == limit ? MessageCursor.Of(delivery(items[^1])) : null);
    }

    /// <summary>Binds the parameters of the shared filters, the cursor, and the limit of a query.</summary>
    public static SqliteCommand WithFilters(
        SqliteCommand command, TenantKey key, IReadOnlyList<Guid>? ids, DeliveryStatus? status, MessageType? type,
        DateTime? createdFromUtc, DateTime? createdToUtc, DateTime? dueBeforeUtc, bool includeTest, MessageCursor? after, int limit) =>
        command
            .With("@tenantId", SqliteValues.Tenant(key.Tenant))
            .With("@ids", ids is null ? null : SqliteValues.Ids(ids))
            .With("@status", status?.ToString())
            .With("@messageType", type?.Name)
            .With("@createdFromUtc", SqliteValues.Time(createdFromUtc))
            .With("@createdToUtc", SqliteValues.Time(createdToUtc))
            .With("@dueBeforeUtc", SqliteValues.Time(dueBeforeUtc))
            .With("@includeTest", includeTest)
            .With("@afterCreatedAtUtc", SqliteValues.Time(after?.CreatedAtUtc))
            .With("@afterId", after is { } cursor ? SqliteValues.Id(cursor.Id) : null)
            .With("@limit", limit);

    public int Purge(string connectionString, DateTime finishedBeforeUtc, int maxCount)
    {
        using var connection = SqliteSchema.Core.Open(connectionString);
        using var command = connection.Command(_purge)
            .With("@finishedBeforeUtc", SqliteValues.Time(finishedBeforeUtc))
            .With("@maxCount", maxCount);
        return command.ExecuteNonQuery();
    }

    public int Retry(string connectionString, TenantKey key, IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries)
    {
        using var connection = SqliteSchema.Core.Open(connectionString);
        using var command = WithIds(connection.Command(_retry), key, ids)
            .With("@nowUtc", SqliteValues.Time(nowUtc))
            .With("@addedTries", addedTries);
        return command.ExecuteNonQuery();
    }

    public int Cancel(string connectionString, TenantKey key, IReadOnlyList<Guid> ids, DateTime nowUtc)
    {
        using var connection = SqliteSchema.Core.Open(connectionString);
        using var command = WithIds(connection.Command(_cancel), key, ids).With("@nowUtc", SqliteValues.Time(nowUtc));
        return command.ExecuteNonQuery();
    }

    /// <param name="connectionString">The connection string.</param>
    /// <param name="query">The filters.</param>
    /// <param name="tenant">The key of a tenant store, or null for all tenants.</param>
    public IReadOnlyList<MessageCount> Count(string connectionString, TrafficQuery query, TenantKey? tenant)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var connection = SqliteSchema.Core.Open(connectionString);
        using var command = connection.Command(_count)
            .With("@tenantId", tenant is { } key ? SqliteValues.Tenant(key.Tenant) : null)
            .With("@createdFromUtc", SqliteValues.Time(query.CreatedFromUtc))
            .With("@createdToUtc", SqliteValues.Time(query.CreatedToUtc))
            .With("@status", query.Status?.ToString())
            .With("@includeTest", query.IncludeTest);
        using var reader = command.ExecuteReader();
        var counts = new List<MessageCount>();
        while (reader.Read())
        {
            counts.Add(CountOf(reader));
        }

        return counts;
    }

    private static SqliteCommand WithIds(SqliteCommand command, TenantKey key, IReadOnlyList<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return command.With("@tenantId", SqliteValues.Tenant(key.Tenant)).With("@ids", SqliteValues.Ids(ids));
    }

    private static MessageCount CountOf(SqliteDataReader row) =>
        new(
            new MessageType(row.GetString(0)),
            row.IsDBNull(1) ? null : new ChannelId(row.GetString(1)),
            Enum.Parse<DeliveryStatus>(row.GetString(2)),
            row.GetBoolean(3),
            row.GetInt32(4),
            row.IsDBNull(5) ? null : SqliteValues.ParseTime(row.GetString(5)));
}
