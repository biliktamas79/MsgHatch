namespace MsgHatch.Subscriptions;

/// <summary>
/// The subscription data of a message type with no generic type, so that the subscription manager can list the message
/// types with a scope function (D81).
/// </summary>
internal interface ISubscriptionMessage
{
    Type EventType { get; }

    bool HasScope { get; }
}
