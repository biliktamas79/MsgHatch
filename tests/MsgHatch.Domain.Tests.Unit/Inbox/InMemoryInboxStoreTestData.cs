using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Domain.Tests.Unit.Inbox;

/// <summary>The test data of the in-memory inbox store. Each test gets a new store, so a reset has nothing to do.</summary>
public sealed class InMemoryInboxStoreTestData(InMemoryInboxStore store) : IInboxStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<ReceivedMessage>> GetStoredAsync(CancellationToken ct) =>
        ValueTask.FromResult(store.GetMessages());
}
