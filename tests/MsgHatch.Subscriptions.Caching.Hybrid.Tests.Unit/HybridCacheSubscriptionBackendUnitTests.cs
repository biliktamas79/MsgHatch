using Microsoft.Extensions.Caching.Hybrid;
using MsgHatch.Domain.Failures;
using MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit;

public sealed class HybridCacheSubscriptionBackendUnitTests : SubscriptionCacheBackendTestsBase<HybridCacheSubscriptionBackend>
{
    public HybridCacheSubscriptionBackendUnitTests()
        : this(new HybridCacheSubscriptionBackendTestData())
    {
    }

    private HybridCacheSubscriptionBackendUnitTests(HybridCacheSubscriptionBackendTestData testData)
        : base(new HybridCacheSubscriptionBackend(testData.Cache), testData)
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
    public async Task Operation_ExceptionOfHybridCache_GivesTheErrorOfItsKind(string operation, bool cancelled, int expectedCode)
    {
        // Arrange
        var backend = new HybridCacheSubscriptionBackend(AHybridCacheThatThrows(cancelled ? new OperationCanceledException() : new InvalidOperationException()));

        // Act
        var error = await CallAsync(backend, operation, new CancellationToken(cancelled));

        // Assert
        Assert.Equal((operation, cancelled, (int?)expectedCode), (operation, cancelled, error?.Type.Code));
    }

    [Fact]
    public async Task TryGetAsync_OperationCanceledExceptionWithNoCancelledToken_GivesUnavailable()
    {
        // Arrange
        var backend = new HybridCacheSubscriptionBackend(AHybridCacheThatThrows(new OperationCanceledException()));

        // Act
        var error = await CallAsync(backend, nameof(ISubscriptionCacheBackend.TryGetAsync), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    private static HybridCache AHybridCacheThatThrows(Exception exception)
    {
        var cache = Substitute.For<HybridCache>();
        cache.GetOrCreateAsync<Func<CancellationToken, ValueTask<HybridCacheSubscriptionEntry?>>, HybridCacheSubscriptionEntry?>(
            default!, default!, default!, default, default, default).ThrowsAsyncForAnyArgs(exception);
        cache.SetAsync<HybridCacheSubscriptionEntry>(default!, default!, default, default, default).ThrowsAsyncForAnyArgs(exception);
        cache.RemoveByTagAsync(default(string)!, default).ThrowsAsyncForAnyArgs(exception);
        return cache;
    }

    private static async Task<Error?> CallAsync(HybridCacheSubscriptionBackend backend, string operation, CancellationToken ct) => operation switch
    {
        nameof(ISubscriptionCacheBackend.TryGetAsync) => (await backend.TryGetAsync("msghatch:sub:n:A:d", ct)).Error,
        nameof(ISubscriptionCacheBackend.SetAsync) => await backend.SetAsync("msghatch:sub:n:A:d", [], ["msghatch:sub:n"], TimeSpan.FromMinutes(1), ct),
        _ => await backend.RemoveByTagAsync("msghatch:sub:n", ct),
    };
}
