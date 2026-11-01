using Microsoft.Extensions.Time.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IOutboxStore"/> and <see cref="IOutboxDispatchStore"/> (section 5.5). Each
/// implementation gets them with one subclass that only builds the store and its test data abstraction.
/// </summary>
/// <typeparam name="TStore">The store, which implements the dispatch store and gives the tenant store of a key.</typeparam>
public abstract partial class OutboxStoreTestsBase<TStore> : IAsyncLifetime
    where TStore : class, IOutboxDispatchStore
{
    protected static readonly TenantKey T1 = TenantKey.For(new TenantId("t1"));
    protected static readonly TenantKey T2 = TenantKey.For(new TenantId("t2"));

    private readonly Func<TStore, TenantKey, IOutboxStore> _tenantStore;

    protected OutboxStoreTestsBase(
        TStore store, Func<TStore, TenantKey, IOutboxStore> tenantStore, Func<TStore, IOutboxStoreTestData> testData)
    {
        Store = store;
        _tenantStore = tenantStore;
        TestData = testData(store);
    }

    protected TStore Store { get; }

    protected IOutboxStoreTestData TestData { get; }

    protected FakeTimeProvider Time { get; } = TestHost.NewFakeTime();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => TestData.ResetAsync(Ct);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return TestData.ResetAsync(Ct);
    }

    protected DateTime Now => Time.GetUtcNow().UtcDateTime;

    protected IOutboxStore TenantStore(TenantKey key) => _tenantStore(Store, key);

    protected OutgoingMessage<TestEnvelope> AMessage(
        TenantKey key = default,
        string text = "text",
        MessageFlags flags = MessageFlags.None,
        Guid? id = null,
        ChannelId? channel = null,
        DateTime? nextTryAtUtc = null) =>
        TestMessages.AnOutgoingMessage(
            new TestEnvelope(text), tenant: key.Tenant, flags: flags, channel: channel ?? TestChannel.Id,
            delivery: TestMessages.ADeliveryState(id) with { CreatedAtUtc = Now, NextTryAtUtc = nextTryAtUtc ?? Now });

    protected async Task<OutgoingMessage<TestEnvelope>> AddAsync(OutgoingMessage<TestEnvelope> message)
    {
        await TenantStore(TenantKey.For(message.Tenant)).AddAsync([message], Ct);
        return message;
    }

    protected Task<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimAsync(int maxCount = 10, ChannelId? channel = null) =>
        Store.ClaimDueAsync(channel ?? TestChannel.Id, maxCount, Now, TimeSpan.FromSeconds(30), Ct).AsTask();

    protected async Task<OutgoingMessage> StoredAsync(Guid id) =>
        (await TestData.GetStoredAsync(Ct)).Single(message => message.Delivery.Id == id);
}
