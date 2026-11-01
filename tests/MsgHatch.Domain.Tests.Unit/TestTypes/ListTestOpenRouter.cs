using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An open generic type that routes a list of its type parameter, not its type parameter. It gives no route.</summary>
/// <typeparam name="T">The item type of the routed list.</typeparam>
public sealed class ListTestOpenRouter<T> : IMessageRouter<List<T>>
{
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<List<T>> message, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([]));
}
