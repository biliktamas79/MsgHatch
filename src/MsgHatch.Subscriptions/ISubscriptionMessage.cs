namespace MsgHatch.Subscriptions;

/// <summary>A message type with a scope function, with no generic type, so that the subscription manager can list them (D81).</summary>
internal interface ISubscriptionMessage
{
    Type EventType { get; }
}
