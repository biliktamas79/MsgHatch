using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>What the outbox store interfaces cannot do: reset the storage, and read the stored state back.</summary>
public interface IOutboxStoreTestData
{
    ValueTask ResetAsync(CancellationToken ct);

    ValueTask<IReadOnlyList<OutgoingMessage>> GetStoredAsync(CancellationToken ct);
}
