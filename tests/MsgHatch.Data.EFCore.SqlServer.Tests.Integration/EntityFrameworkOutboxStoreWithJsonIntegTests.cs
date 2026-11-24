using MsgHatch.Data.EFCore.SqlServer.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Integration;

/// <summary>The outbox contract on SQL Server with the option <c>JsonColumnType = "json"</c>, on a database of the migrations of the package (D323).</summary>
public sealed class EntityFrameworkOutboxStoreWithJsonIntegTests(SqlServerFixture server)
    : OutboxStoreTestsBase<EntityFrameworkOutboxStore>(
        new EntityFrameworkOutboxStore(
            MigratedStores.ContextsOf(MigratedStores.Of(server, "outbox_json", MigratedStores.WithJson), MigratedStores.WithJson),
            new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new EntityFrameworkOutboxStoreTestData(MigratedStores.Of(server, "outbox_json", MigratedStores.WithJson)));
