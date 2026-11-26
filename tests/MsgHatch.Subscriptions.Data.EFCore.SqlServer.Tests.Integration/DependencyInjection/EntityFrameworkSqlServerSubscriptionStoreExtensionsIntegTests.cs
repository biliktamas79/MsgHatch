using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkSqlServerSubscriptionStore</c> and the migration step with both SQL Server packages (D318, D320):
/// each context has its migrations and a history table of its own in the schema of the options. Each test has its own
/// database.
/// </summary>
public sealed class EntityFrameworkSqlServerSubscriptionStoreExtensionsIntegTests(SqlServerFixture server)
{
    private const string _histories = "[msghatch].[__StoreMigrations]";

    private const string _subscriptionHistory = "[msghatch].[__SubscriptionMigrations]";

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
            [["other", "__StoreMigrations"], ["other", "__SubscriptionMigrations"], ["other", "ChannelEntry"],
                ["other", "ChannelState"], ["other", "Inbox"], ["other", "MessageTypeEntry"], ["other", "Outbox"],
                ["other", "Subscription"]],
            await database.RowsAsync("SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_SCHEMA, TABLE_NAME", Ct));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_SecondCall_ChangesNothing()
    {
        // Arrange
        var database = server.Database("twice");
        await using var provider = MigratedSubscriptionStores.Build(database.ConnectionString);
        await provider.MigrateMsgHatchAsync(ct: Ct);
        var schema = await SqlServerSchema.OfAsync(database, Ct, _histories, _subscriptionHistory);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(schema, await SqlServerSchema.OfAsync(database, Ct, _histories, _subscriptionHistory));
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
        await SqlServerSchema.RunScriptAsync(scripted, script, Ct);
        var afterFirstRun = await SqlServerSchema.OfAsync(scripted, Ct, _histories, _subscriptionHistory);
        await SqlServerSchema.RunScriptAsync(scripted, script, Ct);

        // Assert
        Assert.Equal(await SqlServerSchema.OfAsync(migrated, Ct, _histories, _subscriptionHistory), afterFirstRun);
        Assert.Equal(afterFirstRun, await SqlServerSchema.OfAsync(scripted, Ct, _histories, _subscriptionHistory));
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

    private static IServiceCollection StoresOnly(TestDatabase database)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseEntityFrameworkSqlServerStores(database.ConnectionString);
        return services;
    }
}
