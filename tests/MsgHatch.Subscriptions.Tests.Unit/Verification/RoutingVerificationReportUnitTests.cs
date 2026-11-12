using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.Tests.Unit.Verification;

/// <summary>The counts of a routing verification report (section 4.4).</summary>
public sealed class RoutingVerificationReportUnitTests
{
    [Fact]
    public void Counts_RowsOfEachOutcome_CountTheRowsOfEachOutcome()
    {
        // Arrange
        RoutingCheckOutcome[] outcomes =
        [
            RoutingCheckOutcome.Passed, RoutingCheckOutcome.Skipped, RoutingCheckOutcome.Failed, RoutingCheckOutcome.Passed,
            RoutingCheckOutcome.Skipped, RoutingCheckOutcome.Passed,
        ];

        // Act
        var report = new RoutingVerificationReport([.. outcomes.Select(static outcome => ARow(outcome))]);

        // Assert
        Assert.Equal((3, 1, 2), (report.Passed, report.Failed, report.Skipped));
    }

    private static RoutingCheck ARow(RoutingCheckOutcome outcome) =>
        new(new MessageType("A"), "basic", Owner: null, Target: null, Channel: null, "en", outcome, ErrorCode: null, ErrorName: null, Trace: null);
}
