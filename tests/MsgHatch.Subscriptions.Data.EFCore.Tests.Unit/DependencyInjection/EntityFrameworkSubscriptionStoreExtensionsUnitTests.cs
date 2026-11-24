using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
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
        Assert.Equal([["t1", Scenario.S6.Id.ToString("D")]], _database.Rows("SELECT TenantId, Id FROM Subscription"));
    }

    [Fact]
    public async Task UseEntityFrameworkSubscriptionStore_TextCollationOfTheSharedOptions_IsTheCollationOfEachTextColumnAndNoJsonColumn()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:EntityFramework:TextCollation"] = "RTRIM" });
        services.AddMsgHatch().UseEntityFrameworkSubscriptionStore(
            o => o.UseSqlite(_database.ConnectionString).UseEntityFrameworkStoreDialect(new SqliteTestDialect(textCollation: "NOCASE")));
        await using var provider = services.Build();
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var collations = context.GetService<IDesignTimeModel>().Model.GetEntityTypes().SelectMany(static type => type.GetProperties())
            .Where(static property => property.ClrType == typeof(string))
            .Select(static property => $"{(property.Name is "Settings" or "NamedUsers" ? "JSON" : "text")} {property.GetCollation()}")
            .Distinct()
            .Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(["JSON ", "text RTRIM"], collations);
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

    [Theory]
    [InlineData(null, "msghatch")]
    [InlineData("other", "other")]
    public async Task UseEntityFrameworkSubscriptionStore_Schema_HoldsTheTableAndTheHistoryTableWithNoPrefix(string? option, string schema)
    {
        // Arrange
        var services = TestHost.CreateServices(
            configuration: option is null ? null : new Dictionary<string, string?> { ["MsgHatch:EntityFramework:Schema"] = option });
        services.AddMsgHatch().UseEntityFrameworkSubscriptionStore(o => o.UseSqlite(_database.ConnectionString));
        await using var provider = services.Build();
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContextAsync(Ct);
        var model = context.GetService<IDesignTimeModel>().Model;
        var history = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());

        // Act
        string[] tables = [.. model.GetEntityTypes().Select(static type => $"{type.GetSchema()}.{type.GetTableName()}"),
            $"{history.MigrationsHistoryTableSchema}.{history.MigrationsHistoryTableName}"];
        var names = model.GetEntityTypes()
            .SelectMany(static type => type.GetIndexes().Select(static index => index.GetDatabaseName()).Concat(type.GetKeys().Select(static key => key.GetName())))
            .Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal([$"{schema}.Subscription", $"{schema}.__SubscriptionMigrations"], tables);
        Assert.Equal(["PK_Subscription", "UX_Subscription_Owner"], names);
    }

    [Fact]
    public async Task UseEntityFrameworkSubscriptionStore_ClassifierOfTheSubscriptionContext_ClassifiesTheExceptionsOfTheStore()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddScenarioMessages()
            .UseEntityFrameworkSubscriptionStore(o => o.UseSqlite(_database.ConnectionString))
            .AddExceptionClassifier<MsgHatchSubscriptionDbContext, Domain.Tests.Unit.TestTypes.ConflictClassifier<MsgHatchSubscriptionDbContext>>();
        await using var provider = services.Build();
        var store = provider.GetRequiredKeyedService<ISubscriptionStore>(TenantKey.For(new TenantId("t1")));

        // Act
        var result = await store.FindForDeliveryAsync(Scenario.S6.Type, [], [], Ct);

        // Assert
        Assert.Equal(Errors.Conflict, result.Error?.Type);
    }

    [Theory]
    [InlineData(null, "TEXT")]
    [InlineData("json", "json")]
    public async Task UseEntityFrameworkSubscriptionStore_JsonColumnTypeOfTheSharedOptions_IsTheTypeOfEachJsonColumnOnly(string? option, string expected)
    {
        // Arrange
        var services = TestHost.CreateServices(
            configuration: option is null ? null : new Dictionary<string, string?> { ["MsgHatch:EntityFramework:JsonColumnType"] = option });
        services.AddMsgHatch().UseEntityFrameworkSubscriptionStore(o => o.UseSqlite(_database.ConnectionString));
        await using var provider = services.Build();
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var types = context.GetService<IDesignTimeModel>().Model.GetEntityTypes().SelectMany(static type => type.GetProperties())
            .Where(static property => property.Name is "Settings" or "NamedUsers" or "SettingsType")
            .Select(static property => $"{property.Name} {property.GetColumnType()}")
            .Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal([$"NamedUsers {expected}", $"Settings {expected}", "SettingsType TEXT"], types);
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
