using MsgHatch.Domain.Failures;
using MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ZiggyCreatures.Caching.Fusion;

namespace MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit;

public sealed class FusionCacheSubscriptionBackendUnitTests : SubscriptionCacheBackendTestsBase<FusionCacheSubscriptionBackend>
{
    public FusionCacheSubscriptionBackendUnitTests()
        : this(new FusionCacheSubscriptionBackendTestData())
    {
    }

    private FusionCacheSubscriptionBackendUnitTests(FusionCacheSubscriptionBackendTestData testData)
        : base(new FusionCacheSubscriptionBackend(testData.Caches), testData)
    {
    }

    public static TheoryData<string, bool, int> ExceptionRows => new()
    {
        { nameof(ISubscriptionCacheBackend.TryGetAsync), false, 503 },
        { nameof(ISubscriptionCacheBackend.SetAsync), false, 503 },
        { nameof(ISubscriptionCacheBackend.RemoveByTagAsync), false, 503 },
        { nameof(ISubscriptionCacheBackend.TryGetAsync), true, 460 },
        { nameof(ISubscriptionCacheBackend.SetAsync), true, 460 },
        { nameof(ISubscriptionCacheBackend.RemoveByTagAsync), true, 460 },
    };

    [Theory]
    [MemberData(nameof(ExceptionRows))]
    public async Task Operation_ExceptionOfFusionCache_GivesTheErrorOfItsKind(string operation, bool cancelled, int expectedCode)
    {
        // Arrange
        var backend = new FusionCacheSubscriptionBackend(ACacheProviderThatThrows(cancelled ? new OperationCanceledException() : new InvalidOperationException()));

        // Act
        var error = await CallAsync(backend, operation, new CancellationToken(cancelled));

        // Assert
        Assert.Equal((operation, cancelled, (int?)expectedCode), (operation, cancelled, error?.Type.Code));
    }

    [Fact]
    public async Task TryGetAsync_OperationCanceledExceptionWithNoCancelledToken_GivesUnavailable()
    {
        // Arrange
        var backend = new FusionCacheSubscriptionBackend(ACacheProviderThatThrows(new OperationCanceledException()));

        // Act
        var error = await CallAsync(backend, nameof(ISubscriptionCacheBackend.TryGetAsync), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    private static IFusionCacheProvider ACacheProviderThatThrows(Exception exception)
    {
        var cache = Substitute.For<IFusionCache>();
        cache.TryGetAsync<IReadOnlyList<Subscription>>(default!, default, default).ThrowsAsyncForAnyArgs(exception);
        cache.SetAsync<IReadOnlyList<Subscription>>(default!, default!, default(FusionCacheEntryOptions), default, default).ThrowsAsyncForAnyArgs(exception);
        cache.RemoveByTagAsync(default(string)!, default, default).ThrowsAsyncForAnyArgs(exception);
        var caches = Substitute.For<IFusionCacheProvider>();
        caches.GetCache(FusionCacheSubscriptionBackend.CacheName).Returns(cache);
        return caches;
    }

    private static async Task<Error?> CallAsync(FusionCacheSubscriptionBackend backend, string operation, CancellationToken ct) => operation switch
    {
        nameof(ISubscriptionCacheBackend.TryGetAsync) => (await backend.TryGetAsync("msghatch:sub:n:A:d", ct)).Error,
        nameof(ISubscriptionCacheBackend.SetAsync) => await backend.SetAsync("msghatch:sub:n:A:d", [], ["msghatch:sub:n"], TimeSpan.FromMinutes(1), ct),
        _ => await backend.RemoveByTagAsync("msghatch:sub:n", ct),
    };
}
