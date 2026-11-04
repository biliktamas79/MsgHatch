using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the SQLite store tests: a new database file for each test in the temporary folder <c>msghatch-tests</c>,
/// with its connection string from a configuration layer (D65), and the reads of its raw rows.
/// </summary>
public static class TestDatabases
{
    /// <summary>The options of a new database file, bound from the section <c>MsgHatch:Sqlite</c> of an in-memory configuration.</summary>
    public static SqliteStoreOptions NewOptions() => Bind(NewSection());

    /// <summary>The configuration section of a new database file, with a command timeout of 5 seconds.</summary>
    public static Dictionary<string, string?> NewSection()
    {
        var folder = Path.Combine(Path.GetTempPath(), "msghatch-tests");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{Guid.NewGuid():N}.db");
        return new() { ["MsgHatch:Sqlite:ConnectionString"] = $"Data Source={file};Default Timeout=5" };
    }

    /// <summary>Closes the pooled connections of the database, and deletes its file with its WAL files.</summary>
    public static void Delete(string connectionString)
    {
        using (var connection = new SqliteConnection(connectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        var file = new SqliteConnectionStringBuilder(connectionString).DataSource;
        File.Delete(file);
        File.Delete($"{file}-wal");
        File.Delete($"{file}-shm");
    }

    public static SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>The values of each row of a query as text, in the order of the rows. A NULL value is null.</summary>
    public static List<string?[]> Rows(string connectionString, string sql)
    {
        using var connection = Open(connectionString);
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

    public static int Execute(string connectionString, string sql)
    {
        using var connection = Open(connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    /// <summary>A second connection that holds a <c>BEGIN IMMEDIATE</c> transaction with one change, until the test commits or disposes it.</summary>
    public static HeldWrite HoldWrite(string connectionString, string sql)
    {
        var connection = Open(connectionString);
        var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
        return new HeldWrite(connection, transaction);
    }

    /// <summary>The documents of a query, or none when the store has not created its tables yet.</summary>
    public static IEnumerable<byte[]> Documents(string connectionString, string table, string sql) =>
        Rows(connectionString, $"SELECT name FROM sqlite_master WHERE type = 'table' AND name = '{table}'").Count == 0
            ? []
            : Rows(connectionString, sql).Select(static row => System.Text.Encoding.UTF8.GetBytes(row[0]!));

    private static SqliteStoreOptions Bind(Dictionary<string, string?> section) =>
        new ConfigurationBuilder().AddInMemoryCollection(section).Build()
            .GetSection(SqliteStoreOptions.SectionName).Get<SqliteStoreOptions>()!;
}
