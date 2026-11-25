using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Integration.MySql;

/// <summary>The outbox contract on MySQL with no dialect, so the base package runs on a fourth provider (D318).</summary>
public sealed class EntityFrameworkOutboxStoreIntegTests(MySqlFixture server)
    : OutboxStoreTestsBase<EntityFrameworkOutboxStore>(
        new EntityFrameworkOutboxStore(server.Database("outbox").Contexts, new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new MySqlOutboxStoreTestData(server.Database("outbox")));
