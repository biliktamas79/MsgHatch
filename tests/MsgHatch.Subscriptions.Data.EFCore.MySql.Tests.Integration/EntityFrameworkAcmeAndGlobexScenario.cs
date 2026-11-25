using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration;

public sealed class EntityFrameworkAcmeAndGlobexScenario(MySqlFixture server)
    : AcmeAndGlobexScenario<EntityFrameworkSubscriptionStore>(
        MigratedSubscriptionStores.StoreOf(MigratedSubscriptionStores.Of(server, "scenario"), TenantKey.None),
        _ => new MigratedSubscriptionStores.TestData(MigratedSubscriptionStores.Of(server, "scenario")));
