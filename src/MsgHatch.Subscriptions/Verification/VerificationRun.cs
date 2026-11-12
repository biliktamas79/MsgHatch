using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Subscriptions.Resolution;

namespace MsgHatch.Subscriptions.Verification;

/// <summary>One variation of a routing verification: a message type, a test case, and a language (section 4.18).</summary>
internal readonly record struct Variation(MessageType Type, string TestCase, string Language);

/// <summary>
/// The state of one call of <see cref="IRoutingVerifier.VerifyAsync"/>: the subscriptions of the store query, the profiles
/// of the test users, the rows, and the subscriptions that a variation used (section 4.18).
/// </summary>
internal sealed class VerificationRun(
    RoutingVerificationRequest request, IReadOnlyList<Subscription> subscriptions, IReadOnlyList<UserProfile> testProfiles)
{
    private readonly List<RoutingCheck> _checks = [];
    private readonly HashSet<Guid> _used = [];
    private readonly HashSet<MessageType> _verifiedTypes = [];

    public RoutingVerificationRequest Request => request;

    /// <summary>The subscriptions of the store query, in place of the store call of the routing procedure.</summary>
    public InMemorySubscriptionStore Store { get; } = new(subscriptions);

    public UserProfile? ProfileOf(UserId user) => testProfiles.FirstOrDefault(profile => profile.Id == user);

    public void SkipMessageTypeWithNoTestEvent(MessageType type) =>
        _checks.Add(new RoutingCheck(type, null, null, null, null, null, RoutingCheckOutcome.Skipped, null, null, null));

    public void StartMessageType(MessageType type) => _verifiedTypes.Add(type);

    /// <summary>Adds a failed row for the error of one variation (D160, D161), and gives back only a cancellation.</summary>
    public Error? Fail(Variation variation, Error error)
    {
        if (error.Type == Errors.OperationCancelled)
        {
            return error;
        }

        _checks.Add(new RoutingCheck(
            variation.Type, variation.TestCase, null, null, null, variation.Language, RoutingCheckOutcome.Failed, error.Type.Code,
            error.Type.Name, null));
        return null;
    }

    public void Add(Variation variation, SubscriptionResolution resolution, IReadOnlyList<RouteResult> results)
    {
        foreach (var decision in resolution.OptOuts)
        {
            Use(decision.OptOut, decision.Skipped);
        }

        if (results.Count == 0)
        {
            AddNoDelivery(variation, resolution.OptOuts);
            return;
        }

        foreach (var result in results)
        {
            var trace = (ResolutionTrace)result.Route.Trace!;
            Use(trace.Applied, trace.Skipped);
            _checks.Add(new RoutingCheck(
                variation.Type, variation.TestCase, trace.Applied.Owner, result.Route.Target, result.Route.Channel, result.Route.Language,
                result.Message is null ? RoutingCheckOutcome.Failed : RoutingCheckOutcome.Passed, result.Error?.Type.Code,
                result.Error?.Type.Name, trace));
        }
    }

    /// <summary>Gives the report, with a skipped row for each subscription of a verified message type that no variation used.</summary>
    public RoutingVerificationReport Report()
    {
        foreach (var subscription in subscriptions)
        {
            if (_verifiedTypes.Contains(subscription.Type) && !_used.Contains(subscription.Id))
            {
                _checks.Add(new RoutingCheck(
                    subscription.Type, null, subscription.Owner, null, subscription.Channel, null, RoutingCheckOutcome.Skipped, null, null, null));
            }
        }

        return new RoutingVerificationReport(_checks);
    }

    private void AddNoDelivery(Variation variation, IReadOnlyList<OptOutDecision> optOuts) =>
        _checks.Add(new RoutingCheck(
            variation.Type, variation.TestCase, optOuts.Count == 0 ? null : optOuts[0].OptOut.Owner, null, null, variation.Language,
            RoutingCheckOutcome.Passed, null, null, null));

    private void Use(SubscriptionSource applied, IReadOnlyList<SkippedSubscription> skipped)
    {
        _used.Add(applied.Id);
        foreach (var subscription in skipped)
        {
            _used.Add(subscription.Subscription.Id);
        }
    }
}
