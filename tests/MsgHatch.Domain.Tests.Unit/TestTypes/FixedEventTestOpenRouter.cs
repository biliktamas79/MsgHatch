using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An open generic type that routes only <see cref="OtherTestEvent"/>, not its type parameter. It gives no route.</summary>
/// <typeparam name="T">A type parameter that the router does not use.</typeparam>
public sealed class FixedEventTestOpenRouter<T> : IMessageRouter<OtherTestEvent>
{
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<OtherTestEvent> message, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([]));
}
