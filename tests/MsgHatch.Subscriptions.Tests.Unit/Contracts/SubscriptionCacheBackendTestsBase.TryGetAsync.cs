namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class SubscriptionCacheBackendTestsBase<TBackend>
{
    [Fact]
    public async Task TryGetAsync_Miss_GivesNullAndNoError()
    {
        // Act
        var result = await Backend.TryGetAsync("msghatch:sub:t:t1:A:u:3", Ct);

        // Assert
        Assert.Equal((false, (IReadOnlyList<Subscription>?)null), (result.IsError, result.Value));
    }

    [Fact]
    public async Task TryGetAsync_Miss_StoresNoEntry()
    {
        // Arrange
        const string key = "msghatch:sub:t:t1:A:u:4";

        // Act
        await Backend.TryGetAsync(key, Ct);

        // Assert
        Assert.False(await TestData.ContainsAsync(key, Ct));
    }
}
