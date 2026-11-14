namespace WolverineExample.Contracts;

/// <summary>One notification that the hub of the consumer gives back to a client after a connect, for its catch-up.</summary>
/// <param name="MessageType">The message type, which is also the SignalR method of the live notification.</param>
/// <param name="Id">The id of the outgoing message, which the live notification carries too.</param>
/// <param name="Text">The text of the notification.</param>
/// <param name="CreatedAtUtc">The time when the outbox stored the notification.</param>
public sealed record MissedNotification(string MessageType, Guid Id, string Text, DateTime CreatedAtUtc);
