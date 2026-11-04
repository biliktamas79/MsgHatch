using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Resolution;

/// <summary>The result of a resolution: the effective subscriptions as routes, and the opt-out decisions (section 4.4).</summary>
/// <param name="Deliveries">One route for each delivery target and channel, each with its <see cref="ResolutionTrace"/>.</param>
/// <param name="OptOuts">One decision for each opt-out that applies.</param>
public sealed record SubscriptionResolution(IReadOnlyList<MessageRoute> Deliveries, IReadOnlyList<OptOutDecision> OptOuts);
