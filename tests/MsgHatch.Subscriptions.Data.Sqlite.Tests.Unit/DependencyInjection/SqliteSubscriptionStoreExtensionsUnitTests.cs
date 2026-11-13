using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.Sqlite.DependencyInjection;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.Sqlite.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using FixedTenants = MsgHatch.Domain.Tests.Unit.TestTypes.FixedTenants;
using FixedTenantSource = MsgHatch.Domain.Tests.Unit.TestTypes.FixedTenantSource;

namespace MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseSqliteSubscriptionStore</c> registers the SQLite subscription store on the shared SQLite options (D59, D150), and
/// <c>UseSqliteTenantSubscriptionStore</c> one store for each key on the file of that key (D125). Each test has its own database file and folder.
/// </summary>
public sealed class SqliteSubscriptionStoreExtensionsUnitTests : IDisposable
{
    private readonly Dictionary<string, string?> _section = TestDatabases.NewSection();

    private readonly TenantDatabaseFolder _folder = new();

    private string ConnectionString => _section["MsgHatch:Sqlite:ConnectionString"]!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        TestDatabases.Delete(ConnectionString);
        _folder.Dispose();
    }

    [Fact]
    public async Task UseSqliteSubscriptionStore_ConnectionString_ResolvesTheStoreAsItsLoggingWrapperOnTheDatabase()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddScenarioMessages().UseSqliteSubscriptionStore(ConnectionString);
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<ISubscriptionStore>().AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal((1106, Scenario.S6.Id.ToString("D")), (Assert.Single(provider.LogRecords()).Id.Id, StoredId()));
    }

    [Fact]
    public async Task UseSqliteSubscriptionStore_NoArgumentAfterUseSqliteStores_UsesTheConnectionStringOfUseSqliteStores()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddScenarioMessages().UseSqliteStores(ConnectionString).UseSqliteSubscriptionStore();
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<ISubscriptionStore>().AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(Scenario.S6.Id.ToString("D"), StoredId());
    }

    [Fact]
    public async Task UseSqliteSubscriptionStore_ConnectionStringInTheSection_ReachesTheStore()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: _section);
        services.AddMsgHatch().AddScenarioMessages().UseSqliteSubscriptionStore();
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<ISubscriptionStore>().AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(Scenario.S6.Id.ToString("D"), StoredId());
    }

    [Fact]
    public async Task UseSqliteSubscriptionStore_ConnectionStringAlsoInUseSqliteStores_FailsTheStartupValidationAndNamesBothMethods()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseSqliteStores(ConnectionString).UseSqliteSubscriptionStore(ConnectionString);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.StartsWith("UseSqliteStores and UseSqliteSubscriptionStore give ", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseSqliteTenantSubscriptionStore_StoresOfTwoTenantsAndOfNoTenant_WriteEachSubscriptionIntoTheFileOfItsKey()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: _folder.Section);
        services.AddMsgHatch().AddScenarioMessages().UseSqliteTenantSubscriptionStore();
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredKeyedService<ISubscriptionStore>(TenantKey.For(new TenantId("t1"))).AddAsync(Scenario.S6, Ct);
        await provider.GetRequiredKeyedService<ISubscriptionStore>(TenantKey.For(new TenantId("t2"))).AddAsync(Scenario.S6, Ct);
        await provider.GetRequiredService<ISubscriptionStore>().AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(
            [["t1"], ["t2"], [""]],
            [TenantIdsIn("msghatch-t1.db"), TenantIdsIn("msghatch-t2.db"), TenantIdsIn("msghatch.db")]);
    }

    [Fact]
    public async Task UseSqliteTenantSubscriptionStore_ValuesFromCodeAlsoInUseSqliteTenantStores_FailsTheStartupValidationAndNamesBothMethods()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddSingleton(new FixedTenants([]));
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseSqliteTenantStores<FixedTenantSource>(SetFolder)
            .UseSqliteTenantSubscriptionStore(SetFolder);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.StartsWith("UseSqliteTenantStores and UseSqliteTenantSubscriptionStore give ", failure, StringComparison.Ordinal);
    }

    private void SetFolder(MsgHatch.Data.Sqlite.SqliteTenantStoreOptions options)
    {
        options.TenantConnectionString = _folder.Section["MsgHatch:SqliteTenants:TenantConnectionString"]!;
        options.SystemConnectionString = _folder.Section["MsgHatch:SqliteTenants:SystemConnectionString"]!;
    }

    private string?[] TenantIdsIn(string fileName) =>
        [.. TestDatabases.Rows(_folder.ConnectionStringOf(fileName), "SELECT TenantId FROM MsgHatchSubscription").Select(static row => row[0])];

    private string? StoredId() => Assert.Single(Assert.Single(TestDatabases.Rows(ConnectionString, "SELECT Id FROM MsgHatchSubscription")));
}
