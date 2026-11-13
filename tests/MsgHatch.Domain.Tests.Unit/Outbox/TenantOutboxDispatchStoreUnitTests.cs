using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Outbox;

/// <summary>The outbox composite on an in-memory store for each key: the store of <c>TenantKey.None</c>, two tenants, and the other tenants of the contract tests.</summary>
public sealed partial class TenantOutboxDispatchStoreUnitTests : OutboxStoreTestsBase<TenantOutboxDispatchStore>
{
    public TenantOutboxDispatchStoreUnitTests()
        : this(new TenantTestStores())
    {
    }

    private TenantOutboxDispatchStoreUnitTests(TenantTestStores stores)
        : base(stores.OutboxDispatch, (_, key) => stores.OutboxStore(key), _ => new TenantOutboxStoreTestData(stores))
    {
        Stores = stores;
    }

    private TenantTestStores Stores { get; }
}
