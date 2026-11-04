namespace MsgHatch.Subscriptions.Resolution;

/// <summary>A subscription in a resolution trace: its id, its owner with the level, its recipient kind, and its mandatory flag (D92).</summary>
/// <param name="Id">The id of the subscription.</param>
/// <param name="Owner">The owner, with its level.</param>
/// <param name="Recipients">The kind of the recipients.</param>
/// <param name="Mandatory">The mandatory flag.</param>
public readonly record struct SubscriptionSource(Guid Id, SubscriptionOwner Owner, RecipientKind Recipients, bool Mandatory);
