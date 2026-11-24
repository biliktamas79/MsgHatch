using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.PostgreSql.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.PostgreSql;

/// <summary>The subscription store contract on PostgreSQL with no dialect, so the base package runs on a third provider (D318).</summary>
public sealed class EntityFrameworkSubscriptionStoreIntegTests(PostgreSqlFixture server, EntityFrameworkAcmeAndGlobexScenario scenario)
    : SubscriptionStoreTestsBase<EntityFrameworkSubscriptionStore>(
        PostgreSqlSubscriptionStoreTestData.Of(server, "subscriptions").StoreOf,
        _ => PostgreSqlSubscriptionStoreTestData.Of(server, "subscriptions"),
        scenario),
      IClassFixture<EntityFrameworkAcmeAndGlobexScenario>;
