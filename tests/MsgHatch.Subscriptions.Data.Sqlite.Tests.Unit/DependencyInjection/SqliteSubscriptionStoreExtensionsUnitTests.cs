using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.Sqlite.DependencyInjection;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.Sqlite.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit.DependencyInjection;

/// <summary><c>UseSqliteSubscriptionStore</c> registers the SQLite subscription store on the shared SQLite options (D59, D150). Each test has its own database file.</summary>
public sealed class SqliteSubscriptionStoreExtensionsUnitTests : IDisposable
{
    private readonly Dictionary<string, string?> _section = TestDatabases.NewSection();

    private string ConnectionString => _section["MsgHatch:Sqlite:ConnectionString"]!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDatabases.Delete(ConnectionString);

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

    private string? StoredId() => Assert.Single(Assert.Single(TestDatabases.Rows(ConnectionString, "SELECT Id FROM MsgHatchSubscription")));
}
