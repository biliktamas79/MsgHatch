using MsgHatch.Data.EFCore.SqlServer.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Integration;

/// <summary>The inbox contract on SQL Server with the option <c>JsonColumnType = "json"</c>, on a database of the migrations of the package (D323).</summary>
public sealed class EntityFrameworkInboxStoreWithJsonIntegTests(SqlServerFixture server)
    : InboxStoreTestsBase<EntityFrameworkInboxStore>(
        new EntityFrameworkInboxStore(
            MigratedStores.ContextsOf(MigratedStores.Of(server, "inbox_json", MigratedStores.WithJson), MigratedStores.WithJson),
            new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new EntityFrameworkInboxStoreTestData(MigratedStores.Of(server, "inbox_json", MigratedStores.WithJson)));
