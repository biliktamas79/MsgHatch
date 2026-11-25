using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.Tests.Integration.MySql;

/// <summary>The registry settings contract on MySQL with no dialect (D318).</summary>
public sealed class EntityFrameworkRegistrySettingsStoreIntegTests(MySqlFixture server)
    : RegistrySettingsStoreTestsBase<EntityFrameworkRegistrySettingsStore>(
        new EntityFrameworkRegistrySettingsStore(server.Database("registry").Contexts),
        _ => new MySqlRegistrySettingsStoreTestData(server.Database("registry")));
