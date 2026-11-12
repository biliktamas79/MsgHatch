using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Resolution;

namespace MsgHatch.Subscriptions.Verification;

/// <summary>One row of a routing verification report (section 4.18).</summary>
/// <param name="Type">The message type.</param>
/// <param name="TestCase">The test case, or null for a <see cref="RoutingCheckOutcome.Skipped"/> row.</param>
/// <param name="Owner">The owner of the subscription or the opt-out that applied, or of a skipped subscription, or null.</param>
/// <param name="Target">The target of the route, or null when the row has no route.</param>
/// <param name="Channel">The channel of the route or of a skipped subscription, or null.</param>
/// <param name="Language">The language of the route, else the language of the variation, or null for a skipped row.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="ErrorCode">The code of the error of a failed row, for example 404, else null.</param>
/// <param name="ErrorName">The name of the error of a failed row, for example <c>NotFound</c>, else null.</param>
/// <param name="Trace">The resolution trace of the route, or null when the row has no route.</param>
public sealed record RoutingCheck(
    MessageType Type, string? TestCase, SubscriptionOwner? Owner, DeliveryTarget? Target, ChannelId? Channel,
    string? Language, RoutingCheckOutcome Outcome, int? ErrorCode, string? ErrorName, ResolutionTrace? Trace);
