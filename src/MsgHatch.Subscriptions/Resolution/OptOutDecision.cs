using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Resolution;

/// <summary>An opt-out that applies to a target and a channel, so the target gets no message there (D92).</summary>
/// <param name="Target">The target.</param>
/// <param name="Channel">The channel.</param>
/// <param name="OptOut">The opt-out that applies.</param>
/// <param name="Skipped">Each other subscription for the same target and channel, in level order from the default down.</param>
public readonly record struct OptOutDecision(
    DeliveryTarget Target, ChannelId Channel, SubscriptionSource OptOut, IReadOnlyList<SkippedSubscription> Skipped);
