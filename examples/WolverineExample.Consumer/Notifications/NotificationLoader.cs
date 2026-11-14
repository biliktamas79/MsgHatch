using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace WolverineExample.Consumer.Notifications;

/// <summary>Gives one <see cref="Notification"/> for all routes of an event, because its text does not depend on the target.</summary>
/// <typeparam name="TEvent">The domain event.</typeparam>
/// <param name="text">Makes the text of the notification from the event.</param>
public sealed class NotificationLoader<TEvent>(Func<TEvent, string> text) : IPayloadLoader<TEvent, Notification>
{
    /// <inheritdoc/>
    public ValueTask<ErrorOr<IReadOnlyList<Notification>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var notification = new Notification(text(message.Event));
        return ValueTask.FromResult(ErrorOr<IReadOnlyList<Notification>>.ForValue([.. Enumerable.Repeat(notification, routes.Length)]));
    }
}
