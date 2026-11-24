using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkSubscriptionStore</c> on the EF Core SQLite provider: the store runs on each relational
/// provider, and the startup check refuses a provider that is not relational (D182, D318).
/// </summary>
public sealed class EntityFrameworkSubscriptionStoreExtensionsUnitTests
{
    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkSubscriptionStore_Sqlite_PassesTheStartupValidation()
    {
        // Arrange
        await using var provider = Build(o => o.UseSqlite(_database.ConnectionString));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task UseEntityFrameworkSubscriptionStore_Sqlite_StoresTheSubscriptionOfAKeyWithItsTenant()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddScenarioMessages().UseEntityFrameworkSubscriptionStore(o => o.UseSqlite(_database.ConnectionString));
        await using var provider = services.Build();
        await _database.ResetAsync(provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>(), Ct);

        // Act
        var error = await provider.GetRequiredKeyedService<ISubscriptionStore>(TenantKey.For(new TenantId("t1"))).AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal([["t1", Scenario.S6.Id.ToString("D")]], _database.Rows("SELECT TenantId, Id FROM MsgHatchSubscription"));
    }

    [Fact]
    public async Task UseEntityFrameworkSubscriptionStore_ProviderThatIsNotRelational_FailsTheStartupValidationWithTheSetupMethodAndTheProvider()
    {
        // Arrange
        await using var provider = Build(static o => o.UseInMemoryDatabase("msghatch"));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true),
            (failure.Contains("UseEntityFrameworkSubscriptionStore", StringComparison.Ordinal),
                failure.Contains("Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal)));
    }

    private static ServiceProvider Build(Action<DbContextOptionsBuilder> configure)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseInMemoryStores()
            .UseEntityFrameworkSubscriptionStore(configure);
        return services.Build();
    }
}
