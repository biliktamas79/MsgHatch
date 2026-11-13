using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Outbox;

/// <summary>The test data of the outbox composite: the messages and tries of the in-memory store of each key. Each test gets new stores.</summary>
public sealed class TenantOutboxStoreTestData(TenantTestStores stores) : IOutboxStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<OutgoingMessage>> GetStoredAsync(CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<OutgoingMessage>>([.. stores.OutboxStores.SelectMany(static store => store.GetMessages())]);

    public ValueTask<IReadOnlyList<PublishTry>> GetTriesAsync(Guid outgoingMessageId, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<PublishTry>>([.. stores.OutboxStores.SelectMany(store => store.GetTries(outgoingMessageId))]);
}
