namespace WolverineExample.UserSimulator;

/// <summary>The payload of a SignalR notification of the consumer.</summary>
/// <param name="Text">The text of the notification.</param>
public sealed record ReceivedNotification(string Text);
