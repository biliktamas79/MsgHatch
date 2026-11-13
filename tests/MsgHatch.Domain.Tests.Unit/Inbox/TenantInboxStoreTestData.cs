using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Inbox;

/// <summary>The test data of the inbox composite: the messages of the in-memory store of each key. Each test gets new stores.</summary>
public sealed class TenantInboxStoreTestData(TenantTestStores stores) : IInboxStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<ReceivedMessage>> GetStoredAsync(CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ReceivedMessage>>([.. stores.InboxStores.SelectMany(static store => store.GetMessages())]);
}
