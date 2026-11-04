namespace MsgHatch.Subscriptions;

/// <summary>The kind of a subscription.</summary>
public enum SubscriptionKind
{
    /// <summary>A subscription for one channel, with the settings of that channel.</summary>
    Channel,

    /// <summary>"Not subscribed" at the level of the owner, for one channel or for all channels. It has no settings.</summary>
    OptOut,
}
