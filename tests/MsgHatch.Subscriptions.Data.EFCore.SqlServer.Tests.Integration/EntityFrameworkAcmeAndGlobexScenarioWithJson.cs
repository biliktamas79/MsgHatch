using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration;

/// <summary>The seeded scenario on SQL Server with the option <c>JsonColumnType = "json"</c>, on a database of its own (D323).</summary>
public sealed class EntityFrameworkAcmeAndGlobexScenarioWithJson(SqlServerFixture server)
    : AcmeAndGlobexScenario<EntityFrameworkSubscriptionStore>(
        MigratedSubscriptionStores.StoreOf(
            MigratedSubscriptionStores.Of(server, "scenario_json", MigratedSubscriptionStores.WithJson), TenantKey.None, MigratedSubscriptionStores.WithJson),
        _ => new MigratedSubscriptionStores.TestData(MigratedSubscriptionStores.Of(server, "scenario_json", MigratedSubscriptionStores.WithJson)));
