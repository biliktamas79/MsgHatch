using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Resolution;

/// <summary>
/// The route trace of the subscription router (D92, D98). It tells where each decision of an effective subscription
/// comes from. It holds ids, levels, and sources only, so the logs can contain it (D37).
/// </summary>
/// <param name="Applied">The subscription that gave the delivery. The channel and the settings come from it, whole.</param>
/// <param name="Target">Why the target gets the message.</param>
/// <param name="Language">Which rule gave the language.</param>
/// <param name="Address">Where the address comes from.</param>
/// <param name="Skipped">Each other subscription for the same target and channel, in level order from the default down.</param>
public sealed record ResolutionTrace(
    SubscriptionSource Applied, TargetSource Target, LanguageSource Language, AddressSource Address,
    IReadOnlyList<SkippedSubscription> Skipped)
    : RouteTrace;
