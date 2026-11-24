using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Unit;

public sealed class EntityFrameworkAcmeAndGlobexScenario()
    : AcmeAndGlobexScenario<EntityFrameworkSubscriptionStore>(
        EntityFrameworkSubscriptionStoreTestData.StoresOnANewFile()(TenantKey.None),
        EntityFrameworkSubscriptionStoreTestData.Of);
