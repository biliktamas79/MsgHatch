using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.Tests.Integration.PostgreSql;

/// <summary>The registry settings contract on PostgreSQL with no dialect (D318).</summary>
public sealed class EntityFrameworkRegistrySettingsStoreIntegTests(PostgreSqlFixture server)
    : RegistrySettingsStoreTestsBase<EntityFrameworkRegistrySettingsStore>(
        new EntityFrameworkRegistrySettingsStore(server.Database("registry").Contexts),
        _ => new PostgreSqlRegistrySettingsStoreTestData(server.Database("registry")));
