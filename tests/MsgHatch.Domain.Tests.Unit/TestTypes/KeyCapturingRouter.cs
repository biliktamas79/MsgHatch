using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>A router that keeps its constructor arguments, for the tests of the tenant services (D126).</summary>
public sealed class KeyCapturingRouter(
    TenantKey key, IEnvelopeComposer<TestEvent, TestEnvelope> composer, TimeProvider time) : IMessageRouter<TestEvent>
{
    public TenantKey Key { get; } = key;

    public IEnvelopeComposer<TestEvent, TestEnvelope> Composer { get; } = composer;

    public TimeProvider Time { get; } = time;

    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TestEvent> message, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([]));
}
