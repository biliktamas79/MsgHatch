using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="ISubscriptionStore"/> (section 5.5), with the store examples S1 to S13 of
/// <c>docs/subscription-domain.md</c>. Each implementation gets them with one subclass that only builds the store of
/// each tenant key, its test data abstraction, and the class fixture of the seeded scenario. A test that writes resets
/// the data before and after it. A test that only reads the scenario reads <see cref="SeededStore"/> (D151).
/// </summary>
/// <typeparam name="TStore">The store type.</typeparam>
public abstract partial class SubscriptionStoreTestsBase<TStore> : IAsyncLifetime
    where TStore : class, ISubscriptionStore
{
    protected static readonly TenantKey T2 = TenantKey.For(new TenantId("t2"));

    private readonly Func<TenantKey, TStore> _storeOf;
    private readonly AcmeAndGlobexScenario<TStore> _scenario;

    protected SubscriptionStoreTestsBase(
        Func<TenantKey, TStore> storeOf, Func<TStore, ISubscriptionStoreTestData> testDataOf, AcmeAndGlobexScenario<TStore> scenario)
    {
        ArgumentNullException.ThrowIfNull(storeOf);
        ArgumentNullException.ThrowIfNull(testDataOf);
        ArgumentNullException.ThrowIfNull(scenario);
        _storeOf = storeOf;
        TestData = testDataOf(storeOf(TenantKey.None));
        _scenario = scenario;
    }

    /// <summary>The store of <see cref="TenantKey.None"/>.</summary>
    protected TStore Store => _storeOf(TenantKey.None);

    /// <summary>The store with the scenario that the class fixture seeded one time. A test only reads it.</summary>
    protected TStore SeededStore => _scenario.Store;

    protected ISubscriptionStoreTestData TestData { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => TestData.ResetAsync(Ct);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return TestData.ResetAsync(Ct);
    }

    protected TStore StoreOf(TenantKey key) => _storeOf(key);

    protected static string Labels(ErrorOr<IReadOnlyList<Subscription>> subscriptions) =>
        string.Join(", ", subscriptions.Value.Select(static subscription => Scenario.Label(subscription.Id)).Order(StringComparer.Ordinal));

    protected static int? CodeOf(Error? error) => error?.Type.Code;
}
