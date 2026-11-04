namespace MsgHatch.Subscriptions.Resolution;

/// <summary>A subscription for the same target and channel that did not apply, with the rule that removed it (D92).</summary>
/// <param name="Subscription">The skipped subscription.</param>
/// <param name="Reason">The rule that removed it.</param>
public readonly record struct SkippedSubscription(SubscriptionSource Subscription, SkipReason Reason);
