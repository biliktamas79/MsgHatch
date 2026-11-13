using MsgHatch.Data.EntityFrameworkCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EntityFrameworkCore.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EntityFrameworkCore.Tests.Integration;

public sealed class EntityFrameworkAcmeAndGlobexScenario(SqlServerFixture server)
    : AcmeAndGlobexScenario<EntityFrameworkSubscriptionStore>(
        EntityFrameworkSubscriptionStoreTestData.Of(server, "scenario").StoreOf(TenantKey.None),
        _ => EntityFrameworkSubscriptionStoreTestData.Of(server, "scenario"));
