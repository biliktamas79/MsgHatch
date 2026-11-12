namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>The data rows of <see cref="SubscriptionCacheBackendTestsBase{TBackend}"/>, apart from the generic class.</summary>
public static class SubscriptionCacheBackendTestRows
{
    public static TheoryData<string, bool[]> RemovedTagRows => new()
    {
        { "msghatch:sub:t:t1:u:1", [false, true, true] },
        { "msghatch:sub:t:t1", [false, false, true] },
        { "msghatch:sub:t:t3", [true, true, true] },
    };
}
