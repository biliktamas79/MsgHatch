namespace MsgHatch.Subscriptions;

/// <summary>The owner level of a subscription (D72, D97).</summary>
public enum SubscriptionLevel
{
    /// <summary>The owner is the whole tenant, or the whole system with no tenant.</summary>
    Default,

    /// <summary>The owner is one company.</summary>
    Company,

    /// <summary>The owner is one user.</summary>
    User,
}
