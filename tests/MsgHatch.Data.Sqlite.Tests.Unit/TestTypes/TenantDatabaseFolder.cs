using Microsoft.Data.Sqlite;

namespace MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;

/// <summary>
/// A new folder in the temporary folder <c>msghatch-tests</c> for the database files of one test with a database for
/// each tenant (D125), with the configuration section of <see cref="SqliteTenantStoreOptions"/>. The stores do not
/// create a folder, so the test creates it, and deletes it with its files at the end.
/// </summary>
public sealed class TenantDatabaseFolder : IDisposable
{
    public TenantDatabaseFolder()
    {
        Directory.CreateDirectory(FolderPath);
    }

    public string FolderPath { get; } = Path.Combine(Path.GetTempPath(), "msghatch-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The files <c>msghatch-{tenant}.db</c> and <c>msghatch.db</c> in the folder, with a command timeout of 5 seconds.</summary>
    public Dictionary<string, string?> Section => new()
    {
        ["MsgHatch:SqliteTenants:TenantConnectionString"] = $"{ConnectionStringOf("msghatch-{tenant}.db")}",
        ["MsgHatch:SqliteTenants:SystemConnectionString"] = $"{ConnectionStringOf("msghatch.db")}",
    };

    /// <summary>The names of the database files in the folder, in ordinal order.</summary>
    public IReadOnlyList<string> FileNames =>
        [.. Directory.GetFiles(FolderPath, "*.db").Select(static file => Path.GetFileName(file)).Order(StringComparer.Ordinal)];

    public string ConnectionStringOf(string fileName) => $"Data Source={Path.Combine(FolderPath, fileName)};Default Timeout=5";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(FolderPath, recursive: true);
    }
}
