using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>A payload loader that keeps its tenant key, for the tests of the generic setup method. It loads no payload.</summary>
public sealed class KeyCapturingPayloadLoader(TenantKey key) : IPayloadLoader<TestEvent, TestEnvelope>
{
    public TenantKey Key { get; } = key;

    public ValueTask<ErrorOr<IReadOnlyList<TestEnvelope>>> LoadAsync(
        ReceivedMessage<TestEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<TestEnvelope>>.ForValue([]));
}
