using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An open generic router for <c>UseRouter</c>. It gives no route.</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
public sealed class TestOpenRouter<TEvent> : IMessageRouter<TEvent>
{
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TEvent> message, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([]));
}
