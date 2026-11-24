using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>The seeded scenario on PostgreSQL with the option <c>JsonColumnType = "jsonb"</c>, on a database of its own (D323).</summary>
public sealed class EntityFrameworkAcmeAndGlobexScenarioWithJsonb(PostgreSqlFixture server)
    : AcmeAndGlobexScenario<EntityFrameworkSubscriptionStore>(
        MigratedSubscriptionStores.StoreOf(
            MigratedSubscriptionStores.Of(server, "scenario_jsonb", MigratedSubscriptionStores.WithJsonb), TenantKey.None, MigratedSubscriptionStores.WithJsonb),
        _ => new MigratedSubscriptionStores.TestData(MigratedSubscriptionStores.Of(server, "scenario_jsonb", MigratedSubscriptionStores.WithJsonb)));
