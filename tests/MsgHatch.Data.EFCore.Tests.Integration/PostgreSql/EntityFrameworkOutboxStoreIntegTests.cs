using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Integration.PostgreSql;

/// <summary>The outbox contract on PostgreSQL with no dialect, so the claim of the base package runs on a third provider (D318).</summary>
public sealed class EntityFrameworkOutboxStoreIntegTests(PostgreSqlFixture server)
    : OutboxStoreTestsBase<EntityFrameworkOutboxStore>(
        new EntityFrameworkOutboxStore(server.Database("outbox").Contexts, new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new PostgreSqlOutboxStoreTestData(server.Database("outbox")));
