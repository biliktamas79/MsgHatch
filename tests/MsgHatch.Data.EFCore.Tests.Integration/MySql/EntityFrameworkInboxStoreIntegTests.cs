using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

[assembly: AssemblyFixture(typeof(MySqlFixture))]

namespace MsgHatch.Data.EFCore.Tests.Integration.MySql;

/// <summary>
/// The inbox contract on MySQL with no dialect, so the base package runs on a fourth provider (D318). The provider ignores
/// the schema of the model, as a host with no MySQL package sets it.
/// </summary>
public sealed class EntityFrameworkInboxStoreIntegTests(MySqlFixture server)
    : InboxStoreTestsBase<EntityFrameworkInboxStore>(
        new EntityFrameworkInboxStore(server.Database("inbox").Contexts, new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new MySqlInboxStoreTestData(server.Database("inbox")));
