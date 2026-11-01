using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>What the inbox store interfaces cannot do: reset the storage, and read the stored state back.</summary>
public interface IInboxStoreTestData
{
    ValueTask ResetAsync(CancellationToken ct);

    ValueTask<IReadOnlyList<ReceivedMessage>> GetStoredAsync(CancellationToken ct);
}
