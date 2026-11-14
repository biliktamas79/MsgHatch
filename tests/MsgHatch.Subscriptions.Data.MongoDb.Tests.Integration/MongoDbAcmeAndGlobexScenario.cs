using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration;

public sealed class MongoDbAcmeAndGlobexScenario(MongoDbFixture server)
    : AcmeAndGlobexScenario<MongoDbSubscriptionStore>(
        MongoDbSubscriptionStoreTestData.Of(server, "scenario").StoreOf(TenantKey.None),
        _ => MongoDbSubscriptionStoreTestData.Of(server, "scenario"));
