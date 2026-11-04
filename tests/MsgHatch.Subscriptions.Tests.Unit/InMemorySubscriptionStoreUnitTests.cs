using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Tests.Unit;

public sealed class InMemorySubscriptionStoreUnitTests()
    : SubscriptionStoreTestsBase<InMemorySubscriptionStore>(
        InMemorySubscriptionStoreTestData.NewStoreOfEachKey(), new InMemorySubscriptionStoreTestData());
