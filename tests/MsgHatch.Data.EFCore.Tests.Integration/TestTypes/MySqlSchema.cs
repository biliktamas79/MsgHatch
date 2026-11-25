namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>The schema of a MySQL test database, with the rows of its history tables (D320).</summary>
public static class MySqlSchema
{
    private const string _columns = """
        SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, CHARACTER_SET_NAME, COLLATION_NAME
        FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()
        ORDER BY CAST(TABLE_NAME AS BINARY), CAST(COLUMN_NAME AS BINARY)
        """;

    private const string _indexes = """
        SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE, SEQ_IN_INDEX, COLUMN_NAME, SUB_PART
        FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE()
        ORDER BY CAST(TABLE_NAME AS BINARY), CAST(INDEX_NAME AS BINARY), SEQ_IN_INDEX
        """;

    /// <summary>The columns and the indexes of the database, and the rows of the given history tables, in a stable order.</summary>
    public static async Task<List<string?[]>> OfAsync(MySqlTestDatabase database, CancellationToken ct, params string[] historyTables)
    {
        List<string?[]> rows = [.. await database.RowsAsync(_columns, ct), .. await database.RowsAsync(_indexes, ct)];
        foreach (var table in historyTables)
        {
            rows.AddRange(await database.RowsAsync($"SELECT `MigrationId` FROM {MySqlTestDatabase.Quote(table)} ORDER BY `MigrationId`", ct));
        }

        return rows;
    }

    /// <summary>The name of each table of the database.</summary>
    public static Task<List<string?[]>> TablesOfAsync(MySqlTestDatabase database, CancellationToken ct) =>
        database.RowsAsync(
            "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() ORDER BY CAST(TABLE_NAME AS BINARY)", ct);
}
