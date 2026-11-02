using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>The composer of the second test channel with the loader of <see cref="TestPayload"/>, as <see cref="PayloadTestComposer"/> (D135).</summary>
public sealed class OtherPayloadTestComposer(IPayloadLoader<TestEvent, TestPayload> loader) : IEnvelopeComposer<TestEvent, OtherTestEnvelope>
{
    public IPayloadLoader<TestEvent, TestPayload> Loader { get; } = loader;

    public ValueTask<ErrorOr<IReadOnlyList<ErrorOr<OtherTestEnvelope>>>> ComposeAsync(
        ReceivedMessage<TestEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<ErrorOr<OtherTestEnvelope>>>.ForValue([]));
}
