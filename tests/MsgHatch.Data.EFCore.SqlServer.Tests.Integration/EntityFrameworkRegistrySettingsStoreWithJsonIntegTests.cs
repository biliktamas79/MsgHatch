using MsgHatch.Data.EFCore.SqlServer.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Integration;

/// <summary>
/// The registry settings contract on SQL Server with the option <c>JsonColumnType = "json"</c>, on a database of the
/// migrations of the package (D323).
/// </summary>
public sealed class EntityFrameworkRegistrySettingsStoreWithJsonIntegTests(SqlServerFixture server)
    : RegistrySettingsStoreTestsBase<EntityFrameworkRegistrySettingsStore>(
        new EntityFrameworkRegistrySettingsStore(
            MigratedStores.ContextsOf(MigratedStores.Of(server, "registry_json", MigratedStores.WithJson), MigratedStores.WithJson)),
        _ => new EntityFrameworkRegistrySettingsStoreTestData(MigratedStores.Of(server, "registry_json", MigratedStores.WithJson)));
