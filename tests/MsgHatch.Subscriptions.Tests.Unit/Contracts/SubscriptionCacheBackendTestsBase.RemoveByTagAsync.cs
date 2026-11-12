namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class SubscriptionCacheBackendTestsBase<TBackend>
{
    private const string _userKey1 = "msghatch:sub:t:t1:A:u:1";
    private const string _userKey2 = "msghatch:sub:t:t1:A:u:2";
    private const string _otherTenantKey = "msghatch:sub:t:t2:A:u:1";

    [Theory]
    [MemberData(nameof(SubscriptionCacheBackendTestRows.RemovedTagRows), MemberType = typeof(SubscriptionCacheBackendTestRows))]
    public async Task RemoveByTagAsync_Tag_RemovesTheEntriesOfThatTagAndKeepsTheOthers(string tag, bool[] expectedCached)
    {
        // Arrange
        await Backend.SetAsync(_userKey1, TwoSubscriptions, [TenantTag, "msghatch:sub:t:t1:u:1"], Expiration, Ct);
        await Backend.SetAsync(_userKey2, TwoSubscriptions, [TenantTag, "msghatch:sub:t:t1:u:2"], Expiration, Ct);
        await Backend.SetAsync(_otherTenantKey, TwoSubscriptions, ["msghatch:sub:t:t2", "msghatch:sub:t:t2:u:1"], Expiration, Ct);

        // Act
        var error = await Backend.RemoveByTagAsync(tag, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(expectedCached, new[] { await IsCachedAsync(_userKey1), await IsCachedAsync(_userKey2), await IsCachedAsync(_otherTenantKey) });
    }
}
