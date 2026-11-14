namespace WolverineExample.UserSimulator;

/// <summary>The payload of a live SignalR notification of the consumer.</summary>
/// <param name="Id">The id of the outgoing message, which a catch-up gives too.</param>
/// <param name="Text">The text of the notification.</param>
public sealed record ReceivedNotification(Guid Id, string Text);
