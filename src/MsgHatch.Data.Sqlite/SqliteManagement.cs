using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Data.Sqlite;

/// <summary>
/// The statements of the delivery management that the inbox and the outbox share (section 4.19): the purge of the
/// finished messages (D128), and the count of the traffic with one row for each message type, channel, status, and test
/// flag (D164). A null tenant parameter counts all tenants.
/// </summary>
/// <param name="table">The message table.</param>
/// <param name="channelColumn">The channel column, or <c>NULL</c> for the inbox.</param>
internal sealed class SqliteManagement(string table, string channelColumn)
{
    public static readonly SqliteManagement Inbox = new("MsgHatchInbox", "NULL");

    public static readonly SqliteManagement Outbox = new("MsgHatchOutbox", "Channel");

    private readonly string _purge = $"""
        DELETE FROM {table} WHERE Sequence IN (
            SELECT Sequence FROM {table}
            WHERE Status IN ('Done', 'Failed', 'Cancelled') AND COALESCE(LastTryAtUtc, CreatedAtUtc) < @finishedBeforeUtc
            ORDER BY COALESCE(LastTryAtUtc, CreatedAtUtc) LIMIT @maxCount)
        """;

    private readonly string _count = $"""
        SELECT MessageType, {channelColumn}, Status, Flags = 'Test', COUNT(*), MIN(IIF(Status = 'Pending', NextTryAtUtc, NULL))
        FROM {table}
        WHERE (@tenantId IS NULL OR TenantId = @tenantId)
            AND (@createdFromUtc IS NULL OR CreatedAtUtc >= @createdFromUtc) AND (@createdToUtc IS NULL OR CreatedAtUtc < @createdToUtc)
            AND (@status IS NULL OR Status = @status) AND (@includeTest OR Flags IS NOT 'Test')
        GROUP BY MessageType, {channelColumn}, Status, Flags = 'Test'
        """;

    public int Purge(string connectionString, DateTime finishedBeforeUtc, int maxCount)
    {
        using var connection = SqliteSchema.Core.Open(connectionString);
        using var command = connection.Command(_purge)
            .With("@finishedBeforeUtc", SqliteValues.Time(finishedBeforeUtc))
            .With("@maxCount", maxCount);
        return command.ExecuteNonQuery();
    }

    /// <param name="connectionString">The connection string.</param>
    /// <param name="query">The filters.</param>
    /// <param name="tenant">The key of a tenant store, or null for all tenants.</param>
    public IReadOnlyList<MessageCount> Count(string connectionString, TrafficQuery query, TenantKey? tenant)
    {
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

    private static MessageCount CountOf(SqliteDataReader row) =>
        new(
            new MessageType(row.GetString(0)),
            row.IsDBNull(1) ? null : new ChannelId(row.GetString(1)),
            Enum.Parse<DeliveryStatus>(row.GetString(2)),
            row.GetBoolean(3),
            row.GetInt32(4),
            row.IsDBNull(5) ? null : SqliteValues.ParseTime(row.GetString(5)));
}
