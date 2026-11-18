using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>The rows of <see cref="RoutingVerifierTestsBase{T}"/>.</summary>
public static class RoutingVerifierTestRows
{
    public static TheoryData<string, string, string[]> ExampleRows => new()
    {
        {
            "X1", "A",
            ["A / basic / Default / u:1 / Email / en / Passed", "A / basic / Default / u:1 / Email / hu / Passed"]
        },
        {
            "X2", "B",
            [
                "B / basic / User 1 / u:1 / Email / en / Passed", "B / basic / Company Acme / c:100 / Email / hu / Passed",
                "B / basic / User 1 / u:1 / Email / en / Passed", "B / basic / Company Acme / c:100 / Email / hu / Passed",
            ]
        },
        {
            "X3", "E",
            ["E / basic / User 1 / none / none / en / Passed", "E / basic / User 1 / none / none / hu / Passed"]
        },
        {
            "X5", "D",
            [
                "D / basic / Company Acme / u:2 / Email / hu / Passed", "D / basic / Company Acme / u:2 / Email / hu / Passed",
                "D / none / Company Globex / none / SignalR / none / Skipped", "D / none / Company Globex / none / Email / none / Skipped",
                "D / none / User 2 / none / Email / none / Skipped",
            ]
        },
    };
}
