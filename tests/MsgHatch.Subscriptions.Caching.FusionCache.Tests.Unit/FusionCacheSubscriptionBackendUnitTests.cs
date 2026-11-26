using MsgHatch.Domain.Failures;
using MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ZiggyCreatures.Caching.Fusion;

namespace MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit;

public sealed class FusionCacheSubscriptionBackendUnitTests : SubscriptionCacheBackendTestsBase<FusionCacheSubscriptionBackend>
{
    private const string _key = "msghatch:sub:n:A:d";
    private const string _tag = "msghatch:sub:n";
    private static readonly string[] _tags = [_tag];

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
        var cache = ACacheThatThrows(cancelled ? new OperationCanceledException() : new InvalidOperationException());
        var backend = new FusionCacheSubscriptionBackend(AProviderOf(cache));
        var ct = new CancellationToken(cancelled);

        // Act
        var error = await CallAsync(backend, operation, ct);

        // Assert
        Assert.Equal((operation, cancelled, (int?)expectedCode), (operation, cancelled, error?.Type.Code));
        await ReceivedOneCallAsync(cache, operation, ct);
    }

    [Fact]
    public async Task TryGetAsync_OperationCanceledExceptionWithNoCancelledToken_GivesUnavailable()
    {
        // Arrange
        var cache = ACacheThatThrows(new OperationCanceledException());
        var backend = new FusionCacheSubscriptionBackend(AProviderOf(cache));

        // Act
        var error = await CallAsync(backend, nameof(ISubscriptionCacheBackend.TryGetAsync), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
        await ReceivedOneCallAsync(cache, nameof(ISubscriptionCacheBackend.TryGetAsync), Ct);
    }

    private static IFusionCache ACacheThatThrows(Exception exception)
    {
        var cache = Substitute.For<IFusionCache>();
        cache.TryGetAsync<IReadOnlyList<Subscription>>(default!, default, default).ThrowsAsyncForAnyArgs(exception);
        cache.SetAsync<IReadOnlyList<Subscription>>(default!, default!, default(FusionCacheEntryOptions), default, default).ThrowsAsyncForAnyArgs(exception);
        cache.RemoveByTagAsync(default(string)!, default, default).ThrowsAsyncForAnyArgs(exception);
        return cache;
    }

    private static IFusionCacheProvider AProviderOf(IFusionCache cache)
    {
        var caches = Substitute.For<IFusionCacheProvider>();
        caches.GetCache(FusionCacheSubscriptionBackend.CacheName).Returns(cache);
        return caches;
    }

    private static async Task<Error?> CallAsync(FusionCacheSubscriptionBackend backend, string operation, CancellationToken ct) => operation switch
    {
        nameof(ISubscriptionCacheBackend.TryGetAsync) => (await backend.TryGetAsync(_key, ct)).Error,
        nameof(ISubscriptionCacheBackend.SetAsync) => await backend.SetAsync(_key, [], _tags, TimeSpan.FromMinutes(1), ct),
        _ => await backend.RemoveByTagAsync(_tag, ct),
    };

    private static async Task ReceivedOneCallAsync(IFusionCache cache, string operation, CancellationToken ct)
    {
        switch (operation)
        {
            case nameof(ISubscriptionCacheBackend.TryGetAsync):
                await cache.Received(1).TryGetAsync<IReadOnlyList<Subscription>>(_key, null, ct);
                break;
            case nameof(ISubscriptionCacheBackend.SetAsync):
                await cache.Received(1).SetAsync(
                    _key,
                    Arg.Is<IReadOnlyList<Subscription>>(subscriptions => subscriptions.Count == 0),
                    Arg.Is<FusionCacheEntryOptions>(options => options.Duration == TimeSpan.FromMinutes(1)),
                    _tags,
                    ct);
                break;
            default:
                await cache.Received(1).RemoveByTagAsync(_tag, null, ct);
                break;
        }
    }
}
