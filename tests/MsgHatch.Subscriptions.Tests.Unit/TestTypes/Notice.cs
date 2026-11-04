namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>The payload of the SignalR and broker registrations of the scenario.</summary>
/// <param name="Target">The id of the delivery target.</param>
public sealed record Notice(string Target);
