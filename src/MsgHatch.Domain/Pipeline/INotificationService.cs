using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Pipeline;

/// <summary>
/// The entry point of the events (section 4.8). A caller resolves it in its own DI scope, for example the scope of
/// an HTTP request or of a broker message. A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Routes and composes one event, and stores its outgoing messages in the outbox of the tenant of the context.
    /// A failure of one route fails only that route. A failure of the router, of the outbox, or an outer error of a
    /// composer fails the event.
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <param name="domainEvent">The domain event.</param>
    /// <param name="context">The message id, the tenant, and the flags of the event (D115).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The counts, or the error. An unregistered event type gives <see cref="Errors.NotFound"/>.</returns>
    ValueTask<ErrorOr<NotificationResult>> ProcessAsync<TEvent>(TEvent domainEvent, MessageContext context, CancellationToken ct);
}
