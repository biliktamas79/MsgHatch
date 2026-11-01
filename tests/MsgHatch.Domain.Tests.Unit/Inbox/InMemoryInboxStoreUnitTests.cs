using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Domain.Tests.Unit.Inbox;

public sealed class InMemoryInboxStoreUnitTests()
    : InboxStoreTestsBase<InMemoryInboxStore>(
        new InMemoryInboxStore(),
        static (store, key) => store.For(key),
        static store => new InMemoryInboxStoreTestData(store));
