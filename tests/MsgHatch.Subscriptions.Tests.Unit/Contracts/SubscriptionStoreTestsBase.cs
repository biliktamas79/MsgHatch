using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="ISubscriptionStore"/> (section 5.5), with the store examples S1 to S12 of
/// <c>docs/subscription-domain.md</c>. Each implementation gets them with one subclass that only builds the store of
/// each tenant key and its test data abstraction.
/// </summary>
/// <typeparam name="TStore">The store type.</typeparam>
public abstract partial class SubscriptionStoreTestsBase<TStore> : IAsyncLifetime
    where TStore : class, ISubscriptionStore
{
    protected static readonly TenantKey T2 = TenantKey.For(new TenantId("t2"));

    private readonly Func<TenantKey, TStore> _storeOf;

    protected SubscriptionStoreTestsBase(Func<TenantKey, TStore> storeOf, ISubscriptionStoreTestData testData)
    {
        ArgumentNullException.ThrowIfNull(storeOf);
        _storeOf = storeOf;
        TestData = testData;
    }

    /// <summary>The store of <see cref="TenantKey.None"/>.</summary>
    protected TStore Store => _storeOf(TenantKey.None);

    protected ISubscriptionStoreTestData TestData { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => TestData.ResetAsync(Ct);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return TestData.ResetAsync(Ct);
    }

    protected TStore StoreOf(TenantKey key) => _storeOf(key);

    protected async Task SeedScenarioAsync()
    {
        foreach (var subscription in Scenario.Subscriptions)
        {
            Assert.Null(await Store.AddAsync(subscription, Ct));
        }
    }

    protected static string Labels(ErrorOr<IReadOnlyList<Subscription>> subscriptions) =>
        string.Join(", ", subscriptions.Value.Select(static subscription => Scenario.Label(subscription.Id)).Order(StringComparer.Ordinal));

    protected static int? CodeOf(Error? error) => error?.Type.Code;
}
