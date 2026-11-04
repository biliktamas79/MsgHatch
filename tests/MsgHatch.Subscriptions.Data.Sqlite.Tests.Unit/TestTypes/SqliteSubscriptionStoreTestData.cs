using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the SQLite subscription store: a new database file in the temporary folder <c>msghatch-tests</c>,
/// with its connection string from a configuration layer (D65). A reset deletes the file with its WAL files.
/// </summary>
public sealed class SqliteSubscriptionStoreTestData(string connectionString) : ISubscriptionStoreTestData
{
    /// <summary>Gives the store of each tenant key on one database.</summary>
    public static Func<TenantKey, SqliteSubscriptionStore> StoresOn(string connectionString) =>
        key => new SqliteSubscriptionStore(connectionString, key, ScenarioContentTypes.SettingsJson);

    public ValueTask ResetAsync(CancellationToken ct)
    {
        TestDatabases.Delete(connectionString);
        return ValueTask.CompletedTask;
    }
}
