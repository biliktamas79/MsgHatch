using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MsgHatch.Data.EntityFrameworkCore.Tests.Integration.TestTypes;

/// <summary>
/// One database of a test class on the SQL Server of the fixture, with the default schema <c>msghatch</c>. The first
/// reset creates it with <c>EnsureCreatedAsync</c>, and each reset deletes the rows of each table. The raw reads use
/// their own connection.
/// </summary>
public sealed class TestDatabase(string connectionString)
{
    private const string _tables = "MsgHatchInbox,MsgHatchOutbox,MsgHatchChannelDown,MsgHatchChannelEntry,MsgHatchMessageTypeEntry";

    private bool _created;

    public string ConnectionString { get; } = connectionString;

    /// <summary>A pooled factory of the contexts of the stores, as a host with no MsgHatch options makes it.</summary>
    public IDbContextFactory<MsgHatchDbContext> Contexts { get; } =
        new PooledDbContextFactory<MsgHatchDbContext>(new DbContextOptionsBuilder<MsgHatchDbContext>().UseSqlServer(connectionString).Options);

    public async ValueTask ResetAsync(CancellationToken ct)
    {
        if (!_created)
        {
            await using var context = await Contexts.CreateDbContextAsync(ct);
            await context.Database.EnsureCreatedAsync(ct);
            _created = true;
        }

        await ExecuteAsync(string.Join(';', _tables.Split(',').Select(static table => $"DELETE FROM [msghatch].[{table}]")), ct);
    }

    public async Task<int> ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The values of each row of a query as text, in the order of the rows. A time is ISO 8601 text with <c>Z</c>, and a NULL value is null.</summary>
    public async Task<List<string?[]>> RowsAsync(string sql, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<string?[]>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(column => Text(reader.GetValue(column)))]);
        }

        return rows;
    }

    private static string? Text(object value) => value switch
    {
        DBNull => null,
        DateTime time => time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
        bool flag => flag ? "1" : "0",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
