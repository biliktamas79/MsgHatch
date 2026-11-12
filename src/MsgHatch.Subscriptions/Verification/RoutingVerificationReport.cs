namespace MsgHatch.Subscriptions.Verification;

/// <summary>
/// The report of a routing verification: one row for each check (section 4.18). A test job can fail when
/// <see cref="Failed"/> is greater than 0.
/// </summary>
/// <param name="Checks">The rows.</param>
public sealed record RoutingVerificationReport(IReadOnlyList<RoutingCheck> Checks)
{
    /// <summary>The number of <see cref="RoutingCheckOutcome.Passed"/> rows.</summary>
    public int Passed => CountOf(RoutingCheckOutcome.Passed);

    /// <summary>The number of <see cref="RoutingCheckOutcome.Failed"/> rows.</summary>
    public int Failed => CountOf(RoutingCheckOutcome.Failed);

    /// <summary>The number of <see cref="RoutingCheckOutcome.Skipped"/> rows.</summary>
    public int Skipped => CountOf(RoutingCheckOutcome.Skipped);

    private int CountOf(RoutingCheckOutcome outcome)
    {
        var count = 0;
        foreach (var check in Checks)
        {
            if (check.Outcome == outcome)
            {
                count++;
            }
        }

        return count;
    }
}
