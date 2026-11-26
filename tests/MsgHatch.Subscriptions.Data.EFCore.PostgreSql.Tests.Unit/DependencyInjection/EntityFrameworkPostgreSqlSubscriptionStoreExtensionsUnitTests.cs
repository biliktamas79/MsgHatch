using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkPostgreSqlSubscriptionStore</c> with no database (D318, D320): the startup validation, the model has no pending
/// change against the migrations of the package, and the startup check of <c>RequireCurrentSchema</c> with a doubled history
/// of the migrations. No test opens a connection, so the connection string names a closed port.
/// </summary>
public sealed class EntityFrameworkPostgreSqlSubscriptionStoreExtensionsUnitTests
{
    private const string _connectionString = "Host=127.0.0.1;Port=1;Database=setup";

    private const string _requireCurrentSchemaKey = "MsgHatch:EntityFramework:RequireCurrentSchema";

    public static TheoryData<string, string?> ModelRows => new()
    {
        { "msghatch", null },
        { "other", "POSIX" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkPostgreSqlSubscriptionStore_Alone_PassesTheStartupValidation()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseInMemoryStores()
            .UseEntityFrameworkPostgreSqlSubscriptionStore(_connectionString);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(ModelRows))]
    public async Task HasPendingModelChanges_SchemaAndCollationOfTheOptions_HasNoChangeAgainstTheMigrationsOfThePackage(string schema, string? collation)
    {
        // Arrange
        await using var provider = MigratedSubscriptionStores.Build(_connectionString, o => (o.Schema, o.TextCollation) = (schema, collation));
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
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [_requireCurrentSchemaKey] = "true" });
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseInMemoryStores()
            .UseEntityFrameworkPostgreSqlSubscriptionStore(_connectionString);
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
