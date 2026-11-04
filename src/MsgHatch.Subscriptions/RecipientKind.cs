namespace MsgHatch.Subscriptions;

/// <summary>The kind of the recipients of a subscription, separate from its level (D89).</summary>
public enum RecipientKind
{
    /// <summary>The users of an event that the owner covers.</summary>
    ScopeUsers,

    /// <summary>The company that the event concerns.</summary>
    Company,

    /// <summary>The users in <see cref="SubscriptionRecipients.Users"/>.</summary>
    NamedUsers,
}
