using MsgHatch.Email.Placeholders;

namespace WolverineExample.Consumer.Notifications;

/// <summary>
/// The payload of the email templates and of the SignalR messages. A SignalR client gets it with the name of the message
/// type as the method name.
/// </summary>
/// <param name="Text">The text of the notification.</param>
[PlaceholderPayload]
public sealed partial record Notification(string Text);
