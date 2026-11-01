using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Domain.Tests.Unit.Outbox;

public sealed class InMemoryOutboxStoreUnitTests()
    : OutboxStoreTestsBase<InMemoryOutboxStore>(
        new InMemoryOutboxStore(),
        static (store, key) => store.For(key),
        static store => new InMemoryOutboxStoreTestData(store));
