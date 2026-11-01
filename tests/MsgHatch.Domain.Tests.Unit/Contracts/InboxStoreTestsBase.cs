using Microsoft.Extensions.Time.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IInboxStore"/> and <see cref="IInboxDispatchStore"/> (section 5.5). Each
/// implementation gets them with one subclass that only builds the store and its test data abstraction.
/// </summary>
/// <typeparam name="TStore">The store, which implements the dispatch store and gives the tenant store of a key.</typeparam>
public abstract partial class InboxStoreTestsBase<TStore> : IAsyncLifetime
    where TStore : class, IInboxDispatchStore
{
    protected static readonly TenantKey T1 = TenantKey.For(new TenantId("t1"));
    protected static readonly TenantKey T2 = TenantKey.For(new TenantId("t2"));

    private readonly Func<TStore, TenantKey, IInboxStore> _tenantStore;

    protected InboxStoreTestsBase(
        TStore store, Func<TStore, TenantKey, IInboxStore> tenantStore, Func<TStore, IInboxStoreTestData> testData)
    {
        Store = store;
        _tenantStore = tenantStore;
        TestData = testData(store);
    }

    protected TStore Store { get; }

    protected IInboxStoreTestData TestData { get; }

    protected FakeTimeProvider Time { get; } = TestHost.NewFakeTime();

    protected DateTime Now => Time.GetUtcNow().UtcDateTime;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => TestData.ResetAsync(Ct);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return TestData.ResetAsync(Ct);
    }

    protected IInboxStore TenantStore(TenantKey key) => _tenantStore(Store, key);

    protected ReceivedMessage<TestEvent> AMessage(
        TenantKey key = default,
        string text = "text",
        MessageFlags flags = MessageFlags.None,
        Guid? id = null,
        DateTime? nextTryAtUtc = null) =>
        TestMessages.AReceivedMessage(
            new TestEvent(text), key.Tenant, flags: flags,
            delivery: TestMessages.ADeliveryState(id) with { CreatedAtUtc = Now, NextTryAtUtc = nextTryAtUtc ?? Now });

    protected async Task<ReceivedMessage<TestEvent>> AddAsync(ReceivedMessage<TestEvent> message)
    {
        await TenantStore(TenantKey.For(message.Tenant)).AddAsync(message, Ct);
        return message;
    }

    protected Task<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimAsync(int maxCount = 10) =>
        Store.ClaimDueAsync(maxCount, Now, TimeSpan.FromSeconds(30), Ct).AsTask();

    protected async Task<ReceivedMessage> StoredAsync(ReceivedMessage message) =>
        (await TestData.GetStoredAsync(Ct)).Single(stored => stored.Tenant == message.Tenant && stored.Delivery.Id == message.Delivery.Id);
}
