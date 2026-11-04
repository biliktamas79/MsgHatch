using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit;

public sealed class SqliteAcmeAndGlobexScenario()
    : AcmeAndGlobexScenario<SqliteSubscriptionStore>(
        SqliteSubscriptionStoreTestData.StoresOn(TestDatabases.NewOptions().ConnectionString)(TenantKey.None),
        static store => new SqliteSubscriptionStoreTestData(store.ConnectionString));
