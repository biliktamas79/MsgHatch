namespace MsgHatch.Subscriptions.Verification;

/// <summary>The result of one check of a routing verification (section 4.18).</summary>
public enum RoutingCheckOutcome
{
    /// <summary>The route gave a message, or the resolution correctly gave no delivery.</summary>
    Passed,

    /// <summary>The route, the routing, or the preview failed, or the directory does not know the test user (D160, D161).</summary>
    Failed,

    /// <summary>A message type with no test event, or a stored subscription that no variation used.</summary>
    Skipped,
}
