using MsgHatch.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>The outbox contract on PostgreSQL with the option <c>JsonColumnType = "jsonb"</c>, on a database of the migrations of the package (D323).</summary>
public sealed class EntityFrameworkOutboxStoreWithJsonbIntegTests(PostgreSqlFixture server)
    : OutboxStoreTestsBase<EntityFrameworkOutboxStore>(
        new EntityFrameworkOutboxStore(
            MigratedStores.ContextsOf(MigratedStores.Of(server, "outbox_jsonb", MigratedStores.WithJsonb), MigratedStores.WithJsonb),
            new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new PostgreSqlOutboxStoreTestData(MigratedStores.Of(server, "outbox_jsonb", MigratedStores.WithJsonb)));
