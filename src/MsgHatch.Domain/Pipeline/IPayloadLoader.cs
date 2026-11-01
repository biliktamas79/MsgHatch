using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Pipeline;

/// <summary>
/// Loads the template payloads of one channel registration. The library composers call it (D96). A loader is a
/// tenant service (D126). A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
public interface IPayloadLoader<TEvent, TPayload>
{
    /// <summary>
    /// Gives one payload for each route, in the order of the routes. It can give the same instance for many routes
    /// when the data does not depend on the target.
    /// </summary>
    /// <param name="message">The received message with the event.</param>
    /// <param name="routes">All routes of the channel, in the order of the router.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The payloads, or the error. An error is the outer error of the composer, and fails the event.</returns>
    ValueTask<ErrorOr<IReadOnlyList<TPayload>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct);
}
