using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkMySqlSubscriptionStore</c> with no database (D318, D320): the startup validation, the model has no pending
/// change against the migrations of the package, and the startup check of <c>RequireCurrentSchema</c> with a doubled history
/// of the migrations. No test opens a connection, so the connection string names a closed port.
/// </summary>
public sealed class EntityFrameworkMySqlSubscriptionStoreExtensionsUnitTests
{
    private const string _connectionString = "Server=127.0.0.1;Port=1;Database=setup";

    private const string _requireCurrentSchemaKey = "MsgHatch:EntityFramework:RequireCurrentSchema";

    public static TheoryData<string?, string?> ModelRows => new()
    {
        { null, null },
        { "utf8mb4_0900_as_cs", null },
        { null, "json" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkMySqlSubscriptionStore_Alone_PassesTheStartupValidation()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseInMemoryStores()
            .UseEntityFrameworkMySqlSubscriptionStore(_connectionString);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(ModelRows))]
    public async Task HasPendingModelChanges_CollationAndJsonTypeOfTheOptions_HasNoChangeAgainstTheMigrationsOfThePackage(string? collation, string? json)
    {
        // Arrange
        await using var provider = MigratedSubscriptionStores.Build(
            _connectionString, o => (o.TextCollation, o.JsonColumnType) = (collation, json));
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var hasChanges = context.Database.HasPendingModelChanges();

        // Assert
        Assert.False(hasChanges);
    }

    [Fact]
    public async Task RequireCurrentSchema_SubscriptionMigrationThatIsNotApplied_FailsTheStartupWithTheContextAndTheMigration()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices(configuration: new Dictionary<string, string?> { [_requireCurrentSchemaKey] = "true" });
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseInMemoryStores()
            .UseEntityFrameworkMySqlSubscriptionStore(_connectionString);
        services.ConfigureDbContext<MsgHatchSubscriptionDbContext>(
            static o => o.ReplaceService<IHistoryRepository, NoAppliedMigrations>(), ServiceLifetime.Singleton);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true),
            (failure.Contains(nameof(MsgHatchSubscriptionDbContext), StringComparison.Ordinal), failure.Contains("_Initial", StringComparison.Ordinal)));
    }
}
