using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// A composer of the event payload of <c>WithEventPayload</c> that gets the loader in its constructor and keeps it, so
/// that a test can compare the loaders of two composers (D135, D139).
/// </summary>
/// <typeparam name="TEnvelope">The envelope type of the channel registration.</typeparam>
public sealed class EventPayloadTestComposer<TEnvelope>(IPayloadLoader<TestEvent, TestEvent> loader) : IEnvelopeComposer<TestEvent, TEnvelope>
{
    public IPayloadLoader<TestEvent, TestEvent> Loader { get; } = loader;

    public ValueTask<ErrorOr<IReadOnlyList<ErrorOr<TEnvelope>>>> ComposeAsync(
        ReceivedMessage<TestEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<ErrorOr<TEnvelope>>>.ForValue([]));
}
