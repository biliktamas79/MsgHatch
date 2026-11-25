using MsgHatch.Data.EFCore.MySql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.MySql.Tests.Integration;

/// <summary>The outbox contract on MySQL with the dialect, on a database of the migrations of the package (D318, D320, D325).</summary>
public sealed class EntityFrameworkOutboxStoreIntegTests(MySqlFixture server)
    : OutboxStoreTestsBase<EntityFrameworkOutboxStore>(
        new EntityFrameworkOutboxStore(MigratedStores.ContextsOf(MigratedStores.Of(server, "outbox")), new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new MySqlOutboxStoreTestData(MigratedStores.Of(server, "outbox")));
