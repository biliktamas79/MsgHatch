namespace MsgHatch.Subscriptions;

/// <summary>
/// The subscription data of one message type: its scope function from <c>WithScope</c> (D16). The subscription router
/// needs it, so a message type with the subscription router and no scope function fails the startup validation.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
internal sealed class SubscriptionMessage<TEvent>(Func<TEvent, NotificationScope> scope)
{
    public Func<TEvent, NotificationScope> Scope { get; } = scope;
}
