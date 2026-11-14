using MsgHatch.Email.Placeholders;

namespace WolverineExample.Consumer.Notifications;

/// <summary>
/// The payload of the email templates and of the SignalR messages. A SignalR client gets it with the name of the message
/// type as the method name, and keeps its id as the cursor of its catch-up (<see cref="NotificationHistory"/>).
/// </summary>
/// <param name="Id">The id of the outgoing message of this payload.</param>
/// <param name="Text">The text of the notification.</param>
[PlaceholderPayload]
public sealed partial record Notification(Guid Id, string Text);
