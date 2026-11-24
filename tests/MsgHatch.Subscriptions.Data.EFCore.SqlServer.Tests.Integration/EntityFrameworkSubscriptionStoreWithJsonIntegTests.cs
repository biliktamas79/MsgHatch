using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration;

/// <summary>
/// The subscription store contract on SQL Server with the option <c>JsonColumnType = "json"</c>, on a database of the
/// migrations of the package (D323).
/// </summary>
public sealed class EntityFrameworkSubscriptionStoreWithJsonIntegTests(SqlServerFixture server, EntityFrameworkAcmeAndGlobexScenarioWithJson scenario)
    : SubscriptionStoreTestsBase<EntityFrameworkSubscriptionStore>(
        key => MigratedSubscriptionStores.StoreOf(
            MigratedSubscriptionStores.Of(server, "subscriptions_json", MigratedSubscriptionStores.WithJson), key, MigratedSubscriptionStores.WithJson),
        _ => new MigratedSubscriptionStores.TestData(MigratedSubscriptionStores.Of(server, "subscriptions_json", MigratedSubscriptionStores.WithJson)),
        scenario),
      IClassFixture<EntityFrameworkAcmeAndGlobexScenarioWithJson>;
