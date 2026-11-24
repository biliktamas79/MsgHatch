using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.PostgreSql.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.PostgreSql;

public sealed class EntityFrameworkAcmeAndGlobexScenario(PostgreSqlFixture server)
    : AcmeAndGlobexScenario<EntityFrameworkSubscriptionStore>(
        PostgreSqlSubscriptionStoreTestData.Of(server, "scenario").StoreOf(TenantKey.None),
        _ => PostgreSqlSubscriptionStoreTestData.Of(server, "scenario"));
