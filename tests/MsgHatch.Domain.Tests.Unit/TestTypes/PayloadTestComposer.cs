using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// A composer of the test channel that gets the loader of <see cref="TestPayload"/> in its constructor and keeps it, so
/// that a test can compare the loaders of two composers (D135).
/// </summary>
public sealed class PayloadTestComposer(IPayloadLoader<TestEvent, TestPayload> loader) : IEnvelopeComposer<TestEvent, TestEnvelope>
{
    public IPayloadLoader<TestEvent, TestPayload> Loader { get; } = loader;

    public ValueTask<ErrorOr<IReadOnlyList<ErrorOr<TestEnvelope>>>> ComposeAsync(
        ReceivedMessage<TestEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<ErrorOr<TestEnvelope>>>.ForValue([]));
}
