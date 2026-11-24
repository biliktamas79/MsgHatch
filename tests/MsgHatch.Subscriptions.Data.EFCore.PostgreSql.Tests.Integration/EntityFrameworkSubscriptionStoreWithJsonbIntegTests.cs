using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>
/// The subscription store contract on PostgreSQL with the option <c>JsonColumnType = "jsonb"</c>, on a database of the
/// migrations of the package (D323).
/// </summary>
public sealed class EntityFrameworkSubscriptionStoreWithJsonbIntegTests(PostgreSqlFixture server, EntityFrameworkAcmeAndGlobexScenarioWithJsonb scenario)
    : SubscriptionStoreTestsBase<EntityFrameworkSubscriptionStore>(
        key => MigratedSubscriptionStores.StoreOf(
            MigratedSubscriptionStores.Of(server, "subs_jsonb", MigratedSubscriptionStores.WithJsonb), key, MigratedSubscriptionStores.WithJsonb),
        _ => new MigratedSubscriptionStores.TestData(MigratedSubscriptionStores.Of(server, "subs_jsonb", MigratedSubscriptionStores.WithJsonb)),
        scenario),
      IClassFixture<EntityFrameworkAcmeAndGlobexScenarioWithJsonb>;
