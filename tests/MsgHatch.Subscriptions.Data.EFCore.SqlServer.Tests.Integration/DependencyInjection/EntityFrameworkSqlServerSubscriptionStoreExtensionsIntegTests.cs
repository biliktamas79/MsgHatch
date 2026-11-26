using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
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
        const string schema = "other";
        var database = server.Database("schema");
        await using var provider = MigratedSubscriptionStores.Build(database.ConnectionString, static o => o.Schema = schema);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(
            [[schema, "__StoreMigrations"], [schema, "__SubscriptionMigrations"], [schema, "ChannelEntry"],
                [schema, "ChannelState"], [schema, "Inbox"], [schema, "MessageTypeEntry"], [schema, "Outbox"],
                [schema, "Subscription"]],
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
}
