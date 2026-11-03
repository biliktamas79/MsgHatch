using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Pipeline;

/// <summary>
/// The payload loader of <c>WithEventPayload</c> (D139). It gives the event of the received message for each route, as
/// one instance, so that the receiver gets the domain event unchanged.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event, which is also the payload type.</typeparam>
internal sealed class EventPayloadLoader<TEvent> : IPayloadLoader<TEvent, TEvent>
{
    public ValueTask<ErrorOr<IReadOnlyList<TEvent>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        return ValueTask.FromResult(ErrorOr<IReadOnlyList<TEvent>>.ForValue([.. Enumerable.Repeat(message.Event, routes.Length)]));
    }
}
