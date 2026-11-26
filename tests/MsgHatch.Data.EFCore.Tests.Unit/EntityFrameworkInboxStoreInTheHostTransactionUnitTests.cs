using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.Tests.Unit;

/// <summary>
/// The inbox contract on the EF Core SQLite provider with the inbox in the transaction of the host (D331, D332): each add
/// stages the row in a new context of the host, and the host saves it. Each other member runs on the same tenant store.
/// </summary>
public sealed class EntityFrameworkInboxStoreInTheHostTransactionUnitTests : InboxStoreTestsBase<EntityFrameworkInboxStore>
{
    public EntityFrameworkInboxStoreInTheHostTransactionUnitTests()
        : this(new SqliteTestDatabase())
    {
    }

    private EntityFrameworkInboxStoreInTheHostTransactionUnitTests(SqliteTestDatabase database)
        : base(
            new EntityFrameworkInboxStore(database.Contexts, new MsgHatchJson(TestContentTypes.ARegistry())),
            (store, key) => new HostSavingInboxStore(store, key, database.NewHostContext),
            _ => new EntityFrameworkInboxStoreTestData(database))
    {
    }
}
