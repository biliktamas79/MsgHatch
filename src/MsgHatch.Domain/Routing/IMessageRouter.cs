using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// Gives the routes of one event (D95). A router is a tenant service: it sees only the data of its tenant key (D126).
/// A router stores nothing and sends nothing, because a preview calls it too.
/// A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
public interface IMessageRouter<TEvent>
{
    /// <summary>Gives the routes of the event.</summary>
    /// <param name="message">The received message with the event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The routes. An empty list sends nothing, and is not an error. An error fails the event.</returns>
    ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TEvent> message, CancellationToken ct);
}
