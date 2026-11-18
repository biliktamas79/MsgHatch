using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>The rows of <see cref="SubscriptionQueriesTestsBase{T}"/>.</summary>
public static class SubscriptionQueriesTestRows
{
    public static TheoryData<string, string?, string?, string?, string> MessageTypeRows => new()
    {
        { "M2: no filter", null, null, null, "A: #1 Default; B: #2 Default; C: #3 Default; D: ; E: " },
        { "M3: user 1", null, null, "1", "A: #1 Default; B: #6 User; C: #3 Default; D: ; E: #7 User" },
        { "M4: company Acme", null, "100", null, "A: #1 Default; B: #4 Company #10 Company; C: #3 Default; D: #11 Company; E: #5 Company" },
        { "M5: B and user 2", "B", null, "2", "B: #4 Company" },
        { "M19: D and user 2", "D", null, "2", "D: #11 Company" },
    };

    public static TheoryData<string, string?, string?, SubscriptionLevel?, string> SubscriptionRows => new()
    {
        { "M7: company Acme", null, "100", null, "#10, #11, #4, #5" },
        { "M8: message type B", "B", null, null, "#10, #2, #4, #6" },
        { "M9: level User", null, null, SubscriptionLevel.User, "#12, #6, #7" },
    };
}
