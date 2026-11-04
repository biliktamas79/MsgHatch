using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>
/// The scenario "Acme and Globex" of section 4 of <c>docs/subscription-domain.md</c>, seeded one time through a store on
/// storage of its own, for the tests of <see cref="SubscriptionStoreTestsBase{TStore}"/> that only read it (D151). It
/// resets the storage before the seed and after the last test of the class. A sealed subclass for each store only builds
/// the store and its test data.
/// </summary>
/// <typeparam name="TStore">The store type.</typeparam>
/// <param name="store">The store on storage of its own.</param>
/// <param name="testDataOf">Gives the test data of the storage of the store.</param>
public abstract class AcmeAndGlobexScenario<TStore>(TStore store, Func<TStore, ISubscriptionStoreTestData> testDataOf) : IAsyncLifetime
    where TStore : class, ISubscriptionStore
{
    private readonly ISubscriptionStoreTestData _testData = testDataOf(store);

    /// <summary>The store with the seeded scenario. A test only reads it.</summary>
    public TStore Store { get; } = store;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _testData.ResetAsync(Ct);
        foreach (var subscription in Scenario.Subscriptions)
        {
            Assert.Null(await Store.AddAsync(subscription, Ct));
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _testData.ResetAsync(Ct);
    }
}
