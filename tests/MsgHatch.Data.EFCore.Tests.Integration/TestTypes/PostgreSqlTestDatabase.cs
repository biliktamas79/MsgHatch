using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using Npgsql;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// One database of a test class on the PostgreSQL of the fixture, with the default schema <c>msghatch</c>. With no
/// dialect, the text columns have the collation of the database (D318). The first reset creates it with
/// <c>EnsureCreatedAsync</c>, or with the create step of the test, for example the migrations of a provider package
/// (D320). Each reset deletes the rows of each table. The raw reads use their own connection.
/// </summary>
/// <param name="connectionString">The connection string of the database.</param>
/// <param name="create">The create step of the database, or null for <c>EnsureCreatedAsync</c> with <see cref="Contexts"/>.</param>
public sealed class PostgreSqlTestDatabase(string connectionString, Func<PostgreSqlTestDatabase, CancellationToken, Task>? create = null)
{
    private static readonly string[] _tables =
        ["Inbox", "Outbox", "ChannelState", "ChannelEntry", "MessageTypeEntry"];

    private bool _created;

    public string ConnectionString { get; } = connectionString;

    /// <summary>A pooled factory of the contexts of the stores from <c>UseEntityFrameworkStores</c> with no dialect.</summary>
    public IDbContextFactory<MsgHatchDbContext> Contexts { get; } =
        ContextsOf<MsgHatchDbContext>(builder => builder.UseEntityFrameworkStores(o => o.UseNpgsql(connectionString)));

    /// <summary>A pooled factory of a MsgHatch context from the setup method of its package.</summary>
    public static IDbContextFactory<TContext> ContextsOf<TContext>(Action<MsgHatchBuilder> setup)
        where TContext : DbContext
    {
        var services = TestHost.CreateServices();
        setup(services.AddMsgHatch());
        return services.Build().GetRequiredService<IDbContextFactory<TContext>>();
    }

    /// <summary>A quoted name of PostgreSQL.</summary>
    public static string Quote(string name) => new NpgsqlCommandBuilder().QuoteIdentifier(name);

    /// <summary>The SQL of a time column as ISO 8601 text with <c>Z</c>, which <c>MsgHatchJson</c> reads.</summary>
    public static string IsoTime(string column) => $"""to_char("{column}" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')""";

    public async ValueTask ResetAsync(CancellationToken ct)
    {
        if (!_created)
        {
            await (create ?? EnsureCreatedAsync)(this, ct);
            _created = true;
        }

        await ExecuteAsync(string.Join(';', _tables.Select(static table => $"""DELETE FROM "msghatch"."{table}" """)), ct);
    }

    /// <summary>Creates the database with no table, for a test of a migration or of a script.</summary>
    public async Task CreateEmptyAsync(CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        var name = builder.Database!;
        builder.Database = "postgres";
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"CREATE DATABASE {Quote(name)}", connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureCreatedAsync(PostgreSqlTestDatabase database, CancellationToken ct)
    {
        await using var context = await database.Contexts.CreateDbContextAsync(ct);
        await context.Database.EnsureCreatedAsync(ct);
    }

    public async Task<int> ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The values of each row of a query as text, in the order of the rows. A time is ISO 8601 text with <c>Z</c>, and a NULL value is null.</summary>
    public async Task<List<string?[]>> RowsAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
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
