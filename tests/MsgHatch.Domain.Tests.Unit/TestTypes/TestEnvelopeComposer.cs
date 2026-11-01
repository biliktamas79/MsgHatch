using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>The composer of the test channel: one <see cref="TestEnvelope"/> for each route.</summary>
public sealed class TestEnvelopeComposer : IEnvelopeComposer<TestEvent, TestEnvelope>
{
    public ValueTask<ErrorOr<IReadOnlyList<ErrorOr<TestEnvelope>>>> ComposeAsync(
        ReceivedMessage<TestEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<ErrorOr<TestEnvelope>>>.ForValue(
            [.. routes.ToArray().Select(route => ErrorOr<TestEnvelope>.ForValue(new TestEnvelope($"{message.Event.Text} for {route.Address}")))]));
}
