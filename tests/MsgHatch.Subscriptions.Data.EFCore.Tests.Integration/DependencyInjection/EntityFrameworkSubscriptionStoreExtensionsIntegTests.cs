using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkSubscriptionStore</c> registers the EF Core subscription store of each tenant key, with its
/// logging wrapper, on the shared <c>EntityFrameworkStoreOptions</c> (D59). Each test has its own database.
/// </summary>
public sealed class EntityFrameworkSubscriptionStoreExtensionsIntegTests(SqlServerFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkSubscriptionStore_Alone_ResolvesTheStoreOfAKeyAsItsLoggingWrapperOnTheDatabase()
    {
        // Arrange
        var database = server.Database("setup_alone");
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddScenarioMessages().UseEntityFrameworkSubscriptionStore(o => o.UseSqlServer(database.ConnectionString));
        await using var provider = services.Build();
        await CreateTablesAsync(provider);

        // Act
        await provider.GetRequiredKeyedService<ISubscriptionStore>(TenantKey.For(new TenantId("t1"))).AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(1106, Assert.Single(MsgHatchLogRecords(provider)).Id.Id);
        Assert.Equal(
            [["t1", Scenario.S6.Id.ToString("D")]],
            await database.RowsAsync("SELECT TenantId, Id FROM [msghatch].[MsgHatchSubscription]", Ct));
    }

    [Fact]
    public async Task UseEntityFrameworkSubscriptionStore_AfterUseEntityFrameworkStoresWithASchema_KeepsTheRowsInThatSchema()
    {
        // Arrange
        var database = server.Database("setup_schema");
        var services = TestHost.CreateServices();
        services.AddMsgHatch()
            .AddScenarioMessages()
            .UseEntityFrameworkStores(o => o.UseSqlServer(database.ConnectionString), o => o.Schema = "shared")
            .UseEntityFrameworkSubscriptionStore(o => o.UseSqlServer(database.ConnectionString));
        await using var provider = services.Build();
        await CreateTablesAsync(provider);

        // Act
        await provider.GetRequiredService<ISubscriptionStore>().AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal([[Scenario.S6.Id.ToString("D")]], await database.RowsAsync("SELECT Id FROM [shared].[MsgHatchSubscription]", Ct));
    }

    private static async Task CreateTablesAsync(ServiceProvider provider)
    {
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContextAsync(Ct);
        await context.Database.EnsureCreatedAsync(Ct);
    }

    private static IEnumerable<Microsoft.Extensions.Logging.Testing.FakeLogRecord> MsgHatchLogRecords(ServiceProvider provider) =>
        provider.LogRecords().Where(static record => record.Category?.StartsWith("MsgHatch.", StringComparison.Ordinal) == true);
}
