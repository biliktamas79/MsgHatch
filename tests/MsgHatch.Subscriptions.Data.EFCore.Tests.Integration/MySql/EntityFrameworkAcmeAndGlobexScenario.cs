using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.MySql.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.MySql;

public sealed class EntityFrameworkAcmeAndGlobexScenario(MySqlFixture server)
    : AcmeAndGlobexScenario<EntityFrameworkSubscriptionStore>(
        MySqlSubscriptionStoreTestData.Of(server, "scenario").StoreOf(TenantKey.None),
        _ => MySqlSubscriptionStoreTestData.Of(server, "scenario"));
