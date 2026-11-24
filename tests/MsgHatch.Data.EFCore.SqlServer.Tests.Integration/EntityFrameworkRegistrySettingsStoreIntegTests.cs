using MsgHatch.Data.EFCore.SqlServer.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Integration;

/// <summary>The registry settings contract on SQL Server with the dialect, on a database of the migrations of the package (D320).</summary>
public sealed class EntityFrameworkRegistrySettingsStoreIntegTests(SqlServerFixture server)
    : RegistrySettingsStoreTestsBase<EntityFrameworkRegistrySettingsStore>(
        new EntityFrameworkRegistrySettingsStore(MigratedStores.ContextsOf(MigratedStores.Of(server, "registry"))),
        _ => new EntityFrameworkRegistrySettingsStoreTestData(MigratedStores.Of(server, "registry")));
