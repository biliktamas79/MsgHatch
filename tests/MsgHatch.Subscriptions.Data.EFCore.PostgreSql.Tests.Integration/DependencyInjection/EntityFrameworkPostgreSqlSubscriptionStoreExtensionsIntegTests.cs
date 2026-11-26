using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkPostgreSqlSubscriptionStore</c> and the migration step with both PostgreSQL packages (D318, D320):
/// each context has its migrations and a history table of its own in the schema of the options. Each test has its own
/// database.
/// </summary>
public sealed class EntityFrameworkPostgreSqlSubscriptionStoreExtensionsIntegTests(PostgreSqlFixture server)
{
    private const string _history = "\"msghatch\".\"__StoreMigrations\"";

    private const string _subscriptionHistory = "\"msghatch\".\"__SubscriptionMigrations\"";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrateMsgHatchAsync_BothPackagesAndASchemaOfTheOption_CreatesTheTablesOfBothWithTwoHistoryTablesInThatSchema()
    {
        // Arrange
        var database = server.Database("schema");
        await using var provider = MigratedSubscriptionStores.Build(database.ConnectionString, static o => o.Schema = "other");

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(
            [["other", "ChannelEntry"], ["other", "ChannelState"], ["other", "Inbox"], ["other", "MessageTypeEntry"],
                ["other", "Outbox"], ["other", "Subscription"], ["other", "__StoreMigrations"],
                ["other", "__SubscriptionMigrations"]],
            await PostgreSqlSchema.TablesOfAsync(database, Ct));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_SecondCall_ChangesNothing()
    {
        // Arrange
        var database = server.Database("twice");
        await using var provider = MigratedSubscriptionStores.Build(database.ConnectionString);
        await provider.MigrateMsgHatchAsync(ct: Ct);
        var schema = await PostgreSqlSchema.OfAsync(database, Ct, _history, _subscriptionHistory);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(schema, await PostgreSqlSchema.OfAsync(database, Ct, _history, _subscriptionHistory));
    }

    [Fact]
    public async Task ScriptMsgHatchMigrationsAsync_EmptyDatabase_CreatesTheSchemaOfTheMigrationsOfBothPackagesAndASecondRunChangesNothing()
    {
        // Arrange
        var scripted = server.Database("script");
        var migrated = server.Database("script_migrated");
        await scripted.CreateEmptyAsync(Ct);
        await using var provider = MigratedSubscriptionStores.Build(scripted.ConnectionString);
        await using (var migrating = MigratedSubscriptionStores.Build(migrated.ConnectionString))
        {
            await migrating.MigrateMsgHatchAsync(ct: Ct);
        }

        var script = await provider.ScriptMsgHatchMigrationsAsync(Ct);

        // Act
        await scripted.ExecuteAsync(script, Ct);
        var afterFirstRun = await PostgreSqlSchema.OfAsync(scripted, Ct, _history, _subscriptionHistory);
        await scripted.ExecuteAsync(script, Ct);

        // Assert
        Assert.Equal(await PostgreSqlSchema.OfAsync(migrated, Ct, _history, _subscriptionHistory), afterFirstRun);
        Assert.Equal(afterFirstRun, await PostgreSqlSchema.OfAsync(scripted, Ct, _history, _subscriptionHistory));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_DialectOfPostgreSql_GivesEachJsonColumnOfTheSubscriptionsTheTypeJson()
    {
        // Arrange
        var database = server.Database("json");
        await using var provider = MigratedSubscriptionStores.Build(database.ConnectionString);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(
            [["NamedUsers", "json"], ["Settings", "json"], ["SettingsType", "character varying"]],
            await database.RowsAsync(
                """
                SELECT column_name, data_type FROM information_schema.columns
                WHERE table_schema = 'msghatch' AND table_name = 'Subscription' AND column_name IN ('Settings', 'NamedUsers', 'SettingsType')
                ORDER BY column_name COLLATE "C"
                """,
                Ct));
    }

    [Fact]
    public async Task RequireCurrentSchema_SubscriptionMigrationThatIsNotApplied_FailsTheStartupWithTheContextAndTheMigration()
    {
        // Arrange
        var database = server.Database("require");
        await using (var stores = StoresOnly(database).Build())
        {
            await stores.MigrateMsgHatchAsync(ct: Ct);
        }

        await using var provider = MigratedSubscriptionStores.Build(database.ConnectionString, static o => o.RequireCurrentSchema = true);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true),
            (failure.Contains(nameof(MsgHatchSubscriptionDbContext), StringComparison.Ordinal), failure.Contains("_Initial", StringComparison.Ordinal)));
    }

    private static IServiceCollection StoresOnly(PostgreSqlTestDatabase database)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseEntityFrameworkPostgreSqlStores(database.ConnectionString);
        return services;
    }
}
