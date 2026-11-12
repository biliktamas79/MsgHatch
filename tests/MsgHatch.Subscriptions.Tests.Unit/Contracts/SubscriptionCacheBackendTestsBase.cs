using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Caching;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="ISubscriptionCacheBackend"/> (section 5.5). Each backend gets them with one subclass that
/// only builds the backend on a new real in-memory cache, and its test data abstraction.
/// </summary>
/// <typeparam name="TBackend">The backend type.</typeparam>
public abstract partial class SubscriptionCacheBackendTestsBase<TBackend> : IAsyncDisposable
    where TBackend : class, ISubscriptionCacheBackend
{
    protected const string TenantTag = "msghatch:sub:t:t1";

    protected static readonly TimeSpan Expiration = TimeSpan.FromMinutes(1);
    protected static readonly IReadOnlyList<Subscription> TwoSubscriptions =
    [
        TestSubscriptions.A(owner: SubscriptionOwner.ForUser(new UserId("1"))),
        TestSubscriptions.A(owner: SubscriptionOwner.ForUser(new UserId("1")), recipients: SubscriptionRecipients.Company),
    ];

    protected SubscriptionCacheBackendTestsBase(TBackend backend, ISubscriptionCacheBackendTestData testData)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(testData);
        Backend = backend;
        TestData = testData;
    }

    protected TBackend Backend { get; }

    protected ISubscriptionCacheBackendTestData TestData { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return TestData.DisposeAsync();
    }

    protected async Task<bool> IsCachedAsync(string key) => (await Backend.TryGetAsync(key, Ct)).Value is not null;
}
