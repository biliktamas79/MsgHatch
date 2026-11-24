namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>The schema of a PostgreSQL test database, with the rows of its history tables (D320).</summary>
public static class PostgreSqlSchema
{
    private const string _columns = """
        SELECT table_schema, table_name, column_name, data_type, character_maximum_length::text, is_nullable, collation_name
        FROM information_schema.columns WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY table_schema::text COLLATE "C", table_name::text COLLATE "C", column_name::text COLLATE "C"
        """;

    private const string _indexes = """
        SELECT schemaname, tablename, indexname, indexdef FROM pg_indexes
        WHERE schemaname NOT IN ('pg_catalog', 'information_schema') ORDER BY schemaname::text COLLATE "C", tablename::text COLLATE "C", indexname::text COLLATE "C"
        """;

    /// <summary>The columns and the indexes of the database, and the rows of the given history tables, in a stable order.</summary>
    public static async Task<List<string?[]>> OfAsync(PostgreSqlTestDatabase database, CancellationToken ct, params string[] historyTables)
    {
        List<string?[]> rows = [.. await database.RowsAsync(_columns, ct), .. await database.RowsAsync(_indexes, ct)];
        foreach (var table in historyTables)
        {
            rows.AddRange(await database.RowsAsync($"""SELECT "MigrationId" FROM {table} ORDER BY "MigrationId" """, ct));
        }

        return rows;
    }

    /// <summary>The schema and the name of each table of the database, with no table of the system.</summary>
    public static Task<List<string?[]>> TablesOfAsync(PostgreSqlTestDatabase database, CancellationToken ct) =>
        database.RowsAsync(
            """
            SELECT table_schema, table_name FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY table_schema::text COLLATE "C", table_name::text COLLATE "C"
            """,
            ct);
}
