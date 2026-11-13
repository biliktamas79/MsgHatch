using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An outbox tenant store that keeps its tenant key, for the tests of the generic setup method. It stores nothing.</summary>
public sealed class KeyCapturingOutboxStore(TenantKey key) : IOutboxStore
{
    public TenantKey Key { get; } = key;

    public ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct) =>
        ValueTask.FromResult<Error?>(null);

    public ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(new MessagePage<OutgoingMessageSummary>([], Next: null)));

    public ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(null));

    public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<int>.ForValue(0));

    public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<int>.ForValue(0));

    public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue([]));
}
