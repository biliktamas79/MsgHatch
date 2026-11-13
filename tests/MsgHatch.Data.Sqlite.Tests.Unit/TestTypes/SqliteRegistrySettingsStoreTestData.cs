using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;

/// <summary>The test data of the SQLite registry settings store. A reset deletes the database file.</summary>
public sealed class SqliteRegistrySettingsStoreTestData(SqliteRegistrySettingsStore store) : IRegistrySettingsStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct)
    {
        TestDatabases.Delete(store.ConnectionString);
        return ValueTask.CompletedTask;
    }
}
