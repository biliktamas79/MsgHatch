using Microsoft.Extensions.Caching.Hybrid;
using MsgHatch.Domain.Failures;
using MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit;

public sealed class HybridCacheSubscriptionBackendUnitTests : SubscriptionCacheBackendTestsBase<HybridCacheSubscriptionBackend>
{
    private const string _key = "msghatch:sub:n:A:d";
    private const string _tag = "msghatch:sub:n";
    private static readonly string[] _tags = [_tag];

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
        var cache = AHybridCacheThatThrows(cancelled ? new OperationCanceledException() : new InvalidOperationException());
        var backend = new HybridCacheSubscriptionBackend(cache);
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
        var cache = AHybridCacheThatThrows(new OperationCanceledException());
        var backend = new HybridCacheSubscriptionBackend(cache);

        // Act
        var error = await CallAsync(backend, nameof(ISubscriptionCacheBackend.TryGetAsync), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
        await ReceivedOneCallAsync(cache, nameof(ISubscriptionCacheBackend.TryGetAsync), Ct);
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
        nameof(ISubscriptionCacheBackend.TryGetAsync) => (await backend.TryGetAsync(_key, ct)).Error,
        nameof(ISubscriptionCacheBackend.SetAsync) => await backend.SetAsync(_key, [], _tags, TimeSpan.FromMinutes(1), ct),
        _ => await backend.RemoveByTagAsync(_tag, ct),
    };

    private static async Task ReceivedOneCallAsync(HybridCache cache, string operation, CancellationToken ct)
    {
        switch (operation)
        {
            case nameof(ISubscriptionCacheBackend.TryGetAsync):
                await cache.Received(1).GetOrCreateAsync(
                    _key,
                    Arg.Any<Func<CancellationToken, ValueTask<HybridCacheSubscriptionEntry?>>>(),
                    Arg.Any<Func<Func<CancellationToken, ValueTask<HybridCacheSubscriptionEntry?>>, CancellationToken, ValueTask<HybridCacheSubscriptionEntry?>>>(),
                    Arg.Is<HybridCacheEntryOptions?>(options =>
                        options!.Flags == (HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite)),
                    null,
                    ct);
                break;
            case nameof(ISubscriptionCacheBackend.SetAsync):
                await cache.Received(1).SetAsync(
                    _key,
                    Arg.Is<HybridCacheSubscriptionEntry>(entry => entry.Subscriptions.Count == 0),
                    Arg.Is<HybridCacheEntryOptions?>(options => options!.Expiration == TimeSpan.FromMinutes(1)),
                    _tags,
                    ct);
                break;
            default:
                await cache.Received(1).RemoveByTagAsync(_tag, ct);
                break;
        }
    }
}
