using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An open generic router with a constraint that no test event meets. It gives no route.</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
public sealed class ConstrainedTestOpenRouter<TEvent> : IMessageRouter<TEvent>
    where TEvent : IDisposable
{
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TEvent> message, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([]));
}
