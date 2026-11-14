using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace WolverineExample.Consumer.Notifications;

/// <summary>
/// Gives one <see cref="Notification"/> for each route of an event, with the same text, and with the id of the outgoing
/// message of the route. The pipeline makes that id with <see cref="DeterministicId"/> from the same values.
/// </summary>
/// <typeparam name="TEvent">The domain event.</typeparam>
/// <param name="text">Makes the text of the notification from the event.</param>
public sealed class NotificationLoader<TEvent>(Func<TEvent, string> text) : IPayloadLoader<TEvent, Notification>
{
    /// <inheritdoc/>
    public ValueTask<ErrorOr<IReadOnlyList<Notification>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var notificationText = text(message.Event);
        var notifications = new Notification[routes.Length];
        for (var index = 0; index < notifications.Length; index++)
        {
            var route = routes.Span[index];
            notifications[index] = new Notification(
                DeterministicId.Create(message.Delivery.Id, message.Tenant, route.Target, route.Channel), notificationText);
        }

        return ValueTask.FromResult(ErrorOr<IReadOnlyList<Notification>>.ForValue(notifications));
    }
}
