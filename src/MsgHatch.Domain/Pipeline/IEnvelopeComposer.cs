using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Pipeline;

/// <summary>
/// Composes the channel envelopes of one channel registration (D96). A composer is a tenant service (D126).
/// A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
/// <typeparam name="TEnvelope">The type of the channel envelope.</typeparam>
public interface IEnvelopeComposer<TEvent, TEnvelope>
{
    /// <summary>Gives one envelope for each route of the channel, in the order of the routes.</summary>
    /// <param name="message">The received message with the event.</param>
    /// <param name="routes">All routes of the channel, in the order of the router.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// One inner result for each route. An inner error fails only that message. An outer error fails the event.
    /// </returns>
    ValueTask<ErrorOr<IReadOnlyList<ErrorOr<TEnvelope>>>> ComposeAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct);
}
