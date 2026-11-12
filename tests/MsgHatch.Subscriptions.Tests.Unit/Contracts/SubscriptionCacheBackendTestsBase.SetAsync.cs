namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class SubscriptionCacheBackendTestsBase<TBackend>
{
    [Fact]
    public async Task SetAsync_TwoSubscriptions_TryGetAsyncReadsThemBackWhole()
    {
        // Arrange
        const string key = "msghatch:sub:t:t1:A:u:1";
        await Backend.SetAsync(key, TwoSubscriptions, [TenantTag], Expiration, Ct);

        // Act
        var result = await Backend.TryGetAsync(key, Ct);

        // Assert
        Assert.False(result.IsError);
        Assert.Equal(TwoSubscriptions, result.Value);
    }

    [Fact]
    public async Task SetAsync_EmptyList_TryGetAsyncGivesAnEmptyListNotNull()
    {
        // Arrange
        const string key = "msghatch:sub:t:t1:A:u:2";
        await Backend.SetAsync(key, [], [TenantTag], Expiration, Ct);

        // Act
        var result = await Backend.TryGetAsync(key, Ct);

        // Assert
        Assert.Equal((false, 0), (result.IsError, result.Value?.Count));
    }

    [Theory]
    [MemberData(nameof(SubscriptionCacheBackendTestRows.ExpirationRows), MemberType = typeof(SubscriptionCacheBackendTestRows))]
    public async Task SetAsync_ExpirationOfOneMinute_KeepsTheEntryUntilItEnds(int secondsLater, bool expectedCached)
    {
        // Arrange
        const string key = "msghatch:sub:t:t1:A:u:5";
        await Backend.SetAsync(key, TwoSubscriptions, [TenantTag], TimeSpan.FromMinutes(1), Ct);
        TestData.Advance(TimeSpan.FromSeconds(secondsLater));

        // Act
        var cached = await IsCachedAsync(key);

        // Assert
        Assert.Equal((secondsLater, expectedCached), (secondsLater, cached));
    }

    [Fact]
    public async Task SetAsync_NewEntry_GivesNoError()
    {
        // Act
        var error = await Backend.SetAsync("msghatch:sub:t:t1:A:d", TwoSubscriptions, [TenantTag], Expiration, Ct);

        // Assert
        Assert.Null(error);
    }
}
