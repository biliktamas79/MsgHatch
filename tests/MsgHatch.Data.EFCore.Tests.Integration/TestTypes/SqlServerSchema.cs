using System.Text.RegularExpressions;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>The schema of a SQL Server test database, and the run of a SQL script of EF Core on it (D320).</summary>
public static partial class SqlServerSchema
{
    private const string _columns = """
        SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLLATION_NAME
        FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME
        """;

    private const string _indexes = """
        SELECT SCHEMA_NAME(t.schema_id), t.name, i.name, i.type_desc, i.is_unique,
            STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal), i.filter_definition
        FROM sys.indexes AS i JOIN sys.tables AS t ON t.object_id = i.object_id
        JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        GROUP BY t.schema_id, t.name, i.name, i.type_desc, i.is_unique, i.filter_definition
        ORDER BY 1, 2, 3
        """;

    /// <summary>The columns and the indexes of the database, and the rows of the given history tables, in a stable order.</summary>
    public static async Task<List<string?[]>> OfAsync(TestDatabase database, CancellationToken ct, params string[] historyTables)
    {
        List<string?[]> rows = [.. await database.RowsAsync(_columns, ct), .. await database.RowsAsync(_indexes, ct)];
        foreach (var table in historyTables)
        {
            rows.AddRange(await database.RowsAsync($"SELECT MigrationId FROM {table} ORDER BY MigrationId", ct));
        }

        return rows;
    }

    /// <summary>Runs a SQL script batch by batch, with the batch separator <c>GO</c> of the scripts of EF Core.</summary>
    public static async Task RunScriptAsync(TestDatabase database, string script, CancellationToken ct)
    {
        foreach (var batch in BatchSeparator().Split(script).Where(static batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await database.ExecuteAsync(batch, ct);
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();
}
