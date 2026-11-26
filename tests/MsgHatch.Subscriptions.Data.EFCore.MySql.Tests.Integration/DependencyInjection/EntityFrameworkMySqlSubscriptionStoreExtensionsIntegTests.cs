using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkMySqlSubscriptionStore</c> and the migration step with both MySQL packages (D318, D320, D326,
/// D327): each context has its migrations and a history table of its own with the prefix <c>MsgHatch</c> in the database of
/// the connection. Each test has its own database.
/// </summary>
public sealed class EntityFrameworkMySqlSubscriptionStoreExtensionsIntegTests(MySqlFixture server)
{
    private const string _history = "__MsgHatchStoreMigrations";

    private const string _subscriptionHistory = "__MsgHatchSubscriptionMigrations";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrateMsgHatchAsync_BothPackages_CreatesTheTablesOfBothWithThePrefixAndTwoHistoryTablesInTheDatabaseOfTheConnection()
    {
        // Arrange
        var database = server.Database("tables", MigratedSubscriptionStores.TablePrefix);
        await using var provider = MigratedSubscriptionStores.Build(database.ConnectionString);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(
            [["MsgHatchChannelEntry"], ["MsgHatchChannelState"], ["MsgHatchInbox"], ["MsgHatchMessageTypeEntry"], ["MsgHatchOutbox"],
                ["MsgHatchSubscription"], [_history], [_subscriptionHistory]],
            await MySqlSchema.TablesOfAsync(database, Ct));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_BothPackages_GivesTheSubscriptionTheHashKeyAndTheIndexOfTheDeliveryRead()
    {
        // Arrange
        var database = server.Database("indexes", MigratedSubscriptionStores.TablePrefix);
        await using var provider = MigratedSubscriptionStores.Build(database.ConnectionString);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(
            [["IX_Subscription_Type", "1", "1", "TenantId"], ["IX_Subscription_Type", "1", "2", "MessageType"], ["PRIMARY", "0", "1", "Id"],
                ["UX_Subscription_Owner", "0", "1", "OwnerKey"], ["binary(32)"]],
            [.. await database.RowsAsync(
                """
                SELECT INDEX_NAME, NON_UNIQUE, SEQ_IN_INDEX, COLUMN_NAME FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'MsgHatchSubscription' ORDER BY CAST(INDEX_NAME AS BINARY), SEQ_IN_INDEX
                """,
                Ct),
                .. await database.RowsAsync(
                    """
                    SELECT COLUMN_TYPE FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'MsgHatchSubscription' AND COLUMN_NAME = 'OwnerKey'
                    """,
                    Ct)]);
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_SecondCall_ChangesNothing()
    {
        // Arrange
        var database = server.Database("twice", MigratedSubscriptionStores.TablePrefix);
        await using var provider = MigratedSubscriptionStores.Build(database.ConnectionString);
        await provider.MigrateMsgHatchAsync(ct: Ct);
        var schema = await MySqlSchema.OfAsync(database, Ct, _history, _subscriptionHistory);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(schema, await MySqlSchema.OfAsync(database, Ct, _history, _subscriptionHistory));
    }

    [Fact]
    public async Task ScriptMsgHatchMigrationsAsync_EmptyDatabase_CreatesTheSchemaOfTheMigrationsOfBothPackagesAndASecondRunChangesNothing()
    {
        // Arrange
        var scripted = server.Database("script", MigratedSubscriptionStores.TablePrefix);
        var migrated = server.Database("script_migrated", MigratedSubscriptionStores.TablePrefix);
        await scripted.CreateEmptyAsync(Ct);
        await using var provider = MigratedSubscriptionStores.Build(scripted.ConnectionString);
        await using (var migrating = MigratedSubscriptionStores.Build(migrated.ConnectionString))
        {
            await migrating.MigrateMsgHatchAsync(ct: Ct);
        }

        var script = await provider.ScriptMsgHatchMigrationsAsync(Ct);

        // Act
        await scripted.ExecuteScriptAsync(script, Ct);
        var afterFirstRun = await MySqlSchema.OfAsync(scripted, Ct, _history, _subscriptionHistory);
        await scripted.ExecuteScriptAsync(script, Ct);

        // Assert
        Assert.Equal(await MySqlSchema.OfAsync(migrated, Ct, _history, _subscriptionHistory), afterFirstRun);
        Assert.Equal(afterFirstRun, await MySqlSchema.OfAsync(scripted, Ct, _history, _subscriptionHistory));
        Assert.DoesNotContain("ALTER DATABASE", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequireCurrentSchema_SubscriptionMigrationThatIsNotApplied_FailsTheStartupWithTheContextAndTheMigration()
    {
        // Arrange
        var database = server.Database("require", MigratedSubscriptionStores.TablePrefix);
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

    private static IServiceCollection StoresOnly(MySqlTestDatabase database)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseEntityFrameworkMySqlStores(database.ConnectionString);
        return services;
    }
}
