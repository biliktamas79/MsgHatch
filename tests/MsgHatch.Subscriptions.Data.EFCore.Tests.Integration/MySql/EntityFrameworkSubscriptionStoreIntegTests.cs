using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.MySql.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.MySql;

/// <summary>The subscription store contract on MySQL with no dialect, so the base package runs on a fourth provider (D318).</summary>
public sealed class EntityFrameworkSubscriptionStoreIntegTests(MySqlFixture server, EntityFrameworkAcmeAndGlobexScenario scenario)
    : SubscriptionStoreTestsBase<EntityFrameworkSubscriptionStore>(
        MySqlSubscriptionStoreTestData.Of(server, "subscriptions").StoreOf,
        _ => MySqlSubscriptionStoreTestData.Of(server, "subscriptions"),
        scenario),
      IClassFixture<EntityFrameworkAcmeAndGlobexScenario>;
