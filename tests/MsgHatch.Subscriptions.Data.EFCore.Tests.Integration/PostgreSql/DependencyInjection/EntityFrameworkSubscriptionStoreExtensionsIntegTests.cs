using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.PostgreSql.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkSubscriptionStore</c> on PostgreSQL with no dialect registers the EF Core subscription store of
/// each tenant key on the shared <c>EntityFrameworkStoreOptions</c> (D59, D318). Each test has its own database.
/// </summary>
public sealed class EntityFrameworkSubscriptionStoreExtensionsIntegTests(PostgreSqlFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkSubscriptionStore_Alone_ResolvesTheStoreOfAKeyOnTheDatabase()
    {
        // Arrange
        var database = server.Database("setup_alone");
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch().AddScenarioMessages().UseEntityFrameworkSubscriptionStore(o => o.UseNpgsql(database.ConnectionString));
        await using var provider = services.Build();
        await CreateTablesAsync(provider);

        // Act
        await provider.GetRequiredKeyedService<ISubscriptionStore>(TenantKey.For(new TenantId("t1"))).AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(
            [["t1", Scenario.S6.Id.ToString("D")]],
            await database.RowsAsync("""SELECT "TenantId", "Id" FROM "msghatch"."Subscription" """, Ct));
    }

    [Fact]
    public async Task UseEntityFrameworkSubscriptionStore_AfterUseEntityFrameworkStoresWithASchema_KeepsTheRowsInThatSchema()
    {
        // Arrange
        const string schema = "shared";
        var database = server.Database("setup_schema");
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch()
            .AddScenarioMessages()
            .UseEntityFrameworkStores(o => o.UseNpgsql(database.ConnectionString), o => o.Schema = schema)
            .UseEntityFrameworkSubscriptionStore(o => o.UseNpgsql(database.ConnectionString));
        await using var provider = services.Build();
        await CreateTablesAsync(provider);

        // Act
        await provider.GetRequiredService<ISubscriptionStore>().AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal([[Scenario.S6.Id.ToString("D")]], await database.RowsAsync($"""SELECT "Id" FROM "{schema}"."Subscription" """, Ct));
    }

    private static async Task CreateTablesAsync(ServiceProvider provider)
    {
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContextAsync(Ct);
        await context.Database.EnsureCreatedAsync(Ct);
    }
}
