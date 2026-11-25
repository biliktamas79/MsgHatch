using MsgHatch.Data.EFCore.MySql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.MySql.Tests.Integration;

/// <summary>The registry settings contract on MySQL with the dialect, on a database of the migrations of the package (D318, D320).</summary>
public sealed class EntityFrameworkRegistrySettingsStoreIntegTests(MySqlFixture server)
    : RegistrySettingsStoreTestsBase<EntityFrameworkRegistrySettingsStore>(
        new EntityFrameworkRegistrySettingsStore(MigratedStores.ContextsOf(MigratedStores.Of(server, "registry"))),
        _ => new MySqlRegistrySettingsStoreTestData(MigratedStores.Of(server, "registry")));
