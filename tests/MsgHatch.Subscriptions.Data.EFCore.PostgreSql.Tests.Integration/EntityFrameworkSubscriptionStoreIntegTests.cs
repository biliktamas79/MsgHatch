using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>The subscription store contract on PostgreSQL with the dialect, on a database of the migrations of the package (D318, D320).</summary>
public sealed class EntityFrameworkSubscriptionStoreIntegTests(PostgreSqlFixture server, EntityFrameworkAcmeAndGlobexScenario scenario)
    : SubscriptionStoreTestsBase<EntityFrameworkSubscriptionStore>(
        key => MigratedSubscriptionStores.StoreOf(MigratedSubscriptionStores.Of(server, "subscriptions"), key),
        _ => new MigratedSubscriptionStores.TestData(MigratedSubscriptionStores.Of(server, "subscriptions")),
        scenario),
      IClassFixture<EntityFrameworkAcmeAndGlobexScenario>;
