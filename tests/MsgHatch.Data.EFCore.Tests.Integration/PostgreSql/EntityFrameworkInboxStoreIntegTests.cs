using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

[assembly: AssemblyFixture(typeof(PostgreSqlFixture))]

namespace MsgHatch.Data.EFCore.Tests.Integration.PostgreSql;

/// <summary>The inbox contract on PostgreSQL with no dialect, so the claim of the base package runs on a third provider (D318).</summary>
public sealed class EntityFrameworkInboxStoreIntegTests(PostgreSqlFixture server)
    : InboxStoreTestsBase<EntityFrameworkInboxStore>(
        new EntityFrameworkInboxStore(server.Database("inbox").Contexts, new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new PostgreSqlInboxStoreTestData(server.Database("inbox")));
