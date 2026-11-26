using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>
/// A new SQLite database file of one test in the temporary folder <c>msghatch-tests</c> (D71), with a pooled factory of
/// the contexts of the stores on the EF Core SQLite provider. A reset deletes the file, and creates the tables again with
/// <c>EnsureCreatedAsync</c>. The raw reads and writes use their own connection. EF Core writes a time as the text
/// <c>yyyy-MM-dd HH:mm:ss.FFFFFFF</c>.
/// </summary>
public sealed class SqliteTestDatabase
{
    public SqliteTestDatabase()
    {
        var folder = Path.Combine(Path.GetTempPath(), "msghatch-tests");
        Directory.CreateDirectory(folder);
        ConnectionString = $"Data Source={Path.Combine(folder, $"{Guid.NewGuid():N}.db")}";
        Contexts = ContextsOf<MsgHatchDbContext>();
    }

    public string ConnectionString { get; }

    /// <summary>A pooled factory of the contexts of the stores, as a host with no MsgHatch options makes it.</summary>
    public IDbContextFactory<MsgHatchDbContext> Contexts { get; }

    /// <summary>A pooled factory of a MsgHatch context on the file, with an optional change of its options.</summary>
    public IDbContextFactory<TContext> ContextsOf<TContext>(Action<DbContextOptionsBuilder<TContext>>? configure = null)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>().UseSqlite(ConnectionString);
        options.IgnoreManyServiceProvidersWarning();
        configure?.Invoke(options);
        return new PooledDbContextFactory<TContext>(options.Options);
    }

    /// <summary>A new context of a host on the file, with the inbox of <c>MapMsgHatchInbox</c> and no MsgHatch options.</summary>
    internal TestHostContext NewHostContext()
    {
        var options = new DbContextOptionsBuilder<TestHostContext>().UseSqlite(ConnectionString);
        options.IgnoreManyServiceProvidersWarning();
        return new TestHostContext(options.Options);
    }

    /// <summary>The SQL of a time column as ISO 8601 text with <c>Z</c>, which <c>MsgHatchJson</c> reads.</summary>
    public static string IsoTime(string column) => $"replace({column}, ' ', 'T') || 'Z'";

    /// <summary>Deletes the file, and creates the tables of the context on a new file.</summary>
    public async ValueTask ResetAsync<TContext>(IDbContextFactory<TContext> contexts, CancellationToken ct)
        where TContext : DbContext
    {
        Delete();
        await using var context = await contexts.CreateDbContextAsync(ct);
        await context.Database.EnsureCreatedAsync(ct);
    }

    /// <summary>Closes the pooled connections of the file, and deletes it.</summary>
    public void Delete()
    {
        using (var connection = new SqliteConnection(ConnectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        File.Delete(new SqliteConnectionStringBuilder(ConnectionString).DataSource);
    }

    /// <summary>The values of each row of a query as text, in the order of the rows. A NULL value is null.</summary>
    public List<string?[]> Rows(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string?[]>();
        while (reader.Read())
        {
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(column => reader.IsDBNull(column) ? null : reader.GetString(column))]);
        }

        return rows;
    }

    public int Execute(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    /// <summary>Makes each insert into a table fail with a <see cref="SqliteException"/>, as a refused write gives it. The reads still work.</summary>
    public void RefuseInserts(string table) =>
        Execute($"CREATE TRIGGER Refuse{table}Inserts BEFORE INSERT ON {table} BEGIN SELECT RAISE(ABORT, 'refused'); END");

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }
}
