using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Tests.Unit;

public sealed class InMemorySubscriptionStoreUnitTests(InMemoryAcmeAndGlobexScenario scenario)
    : SubscriptionStoreTestsBase<InMemorySubscriptionStore>(
        InMemorySubscriptionStoreTestData.NewStoreOfEachKey(), static _ => new InMemorySubscriptionStoreTestData(), scenario),
      IClassFixture<InMemoryAcmeAndGlobexScenario>;
