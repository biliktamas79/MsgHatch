using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Domain.Tests.Unit.Outbox;

/// <summary>The test data of the in-memory outbox store. Each test gets a new store, so a reset has nothing to do.</summary>
public sealed class InMemoryOutboxStoreTestData(InMemoryOutboxStore store) : IOutboxStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<OutgoingMessage>> GetStoredAsync(CancellationToken ct) =>
        ValueTask.FromResult(store.GetMessages());
}
