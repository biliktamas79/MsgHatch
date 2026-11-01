using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An outbox tenant store that keeps its tenant key, for the tests of the generic setup method. It stores nothing.</summary>
public sealed class KeyCapturingOutboxStore(TenantKey key) : IOutboxStore
{
    public TenantKey Key { get; } = key;

    public ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct) =>
        ValueTask.FromResult<Error?>(null);
}
