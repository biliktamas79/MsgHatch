using MsgHatch.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>
/// The registry settings contract on PostgreSQL with the option <c>JsonColumnType = "jsonb"</c>, on a database of the
/// migrations of the package (D323).
/// </summary>
public sealed class EntityFrameworkRegistrySettingsStoreWithJsonbIntegTests(PostgreSqlFixture server)
    : RegistrySettingsStoreTestsBase<EntityFrameworkRegistrySettingsStore>(
        new EntityFrameworkRegistrySettingsStore(
            MigratedStores.ContextsOf(MigratedStores.Of(server, "registry_jsonb", MigratedStores.WithJsonb), MigratedStores.WithJsonb)),
        _ => new PostgreSqlRegistrySettingsStoreTestData(MigratedStores.Of(server, "registry_jsonb", MigratedStores.WithJsonb)));
