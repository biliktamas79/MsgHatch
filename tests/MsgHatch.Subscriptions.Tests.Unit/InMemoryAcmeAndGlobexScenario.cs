using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Tests.Unit;

public sealed class InMemoryAcmeAndGlobexScenario()
    : AcmeAndGlobexScenario<InMemorySubscriptionStore>(new InMemorySubscriptionStore(), static _ => new InMemorySubscriptionStoreTestData());
