using MsgHatch.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>The registry settings contract on PostgreSQL with the dialect, on a database of the migrations of the package (D320).</summary>
public sealed class EntityFrameworkRegistrySettingsStoreIntegTests(PostgreSqlFixture server)
    : RegistrySettingsStoreTestsBase<EntityFrameworkRegistrySettingsStore>(
        new EntityFrameworkRegistrySettingsStore(MigratedStores.ContextsOf(MigratedStores.Of(server, "registry"))),
        _ => new PostgreSqlRegistrySettingsStoreTestData(MigratedStores.Of(server, "registry")));
