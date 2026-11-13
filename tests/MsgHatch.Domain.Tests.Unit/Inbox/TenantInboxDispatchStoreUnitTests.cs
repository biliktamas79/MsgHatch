using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Inbox;

/// <summary>The inbox composite on an in-memory store for each key: the store of <c>TenantKey.None</c>, two tenants, and the other tenants of the contract tests.</summary>
public sealed partial class TenantInboxDispatchStoreUnitTests : InboxStoreTestsBase<TenantInboxDispatchStore>
{
    public TenantInboxDispatchStoreUnitTests()
        : this(new TenantTestStores())
    {
    }

    private TenantInboxDispatchStoreUnitTests(TenantTestStores stores)
        : base(stores.InboxDispatch, (_, key) => stores.InboxStore(key), _ => new TenantInboxStoreTestData(stores))
    {
        Stores = stores;
    }

    private TenantTestStores Stores { get; }
}
