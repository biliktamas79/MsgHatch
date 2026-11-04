namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>The data rows of <see cref="SubscriptionStoreTestsBase{TStore}"/>, apart from the generic class.</summary>
public static class SubscriptionStoreTestRows
{
    public static TheoryData<string, string?, string?, string?, SubscriptionLevel?, string> QueryRows => new()
    {
        { "S7: message type B", "B", null, null, null, "#10, #2, #4, #6" },
        { "S7: company Acme", null, "100", null, null, "#10, #11, #4, #5" },
        { "S7: user 1", null, null, "1", null, "#6, #7" },
        { "S7: level User", null, null, null, SubscriptionLevel.User, "#12, #6, #7" },
        { "S7: company Acme and level Company", null, "100", null, SubscriptionLevel.Company, "#10, #11, #4, #5" },
        { "S7: no filter", null, null, null, null, "#1, #10, #11, #12, #2, #3, #4, #5, #6, #7, #8, #9" },
        { "message type A and level User", "A", null, null, SubscriptionLevel.User, "" },
    };
}
