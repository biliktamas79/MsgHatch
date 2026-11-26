using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MySqlConnector;

namespace MsgHatch.Data.EFCore.MySql.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkMySqlStores</c> and the migration step on MySQL (D318, D320, D325, D326): the migrations of the
/// package create the tables with the prefix <c>MsgHatch</c> in the database of the connection, the model has no pending
/// change against them, and the script and a migration with its own provider setup give the same schema. Each test has its
/// own database.
/// </summary>
public sealed class EntityFrameworkMySqlStoreExtensionsIntegTests(MySqlFixture server)
{
    private const string _history = "__MsgHatchStoreMigrations";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrateMsgHatchAsync_EmptyDatabase_CreatesTheTablesWithThePrefixInTheDatabaseOfTheConnectionAndNoOtherDatabase()
    {
        // Arrange
        var database = server.Database("tables", MigratedStores.TablePrefix);
        await using var provider = MigratedStores.Build(database.ConnectionString);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(
            [["MsgHatchChannelEntry"], ["MsgHatchChannelState"], ["MsgHatchInbox"], ["MsgHatchMessageTypeEntry"], ["MsgHatchOutbox"], [_history]],
            await MySqlSchema.TablesOfAsync(database, Ct));
        Assert.Equal([["0"]], await database.RowsAsync("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = 'msghatch'", Ct));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_EmptyDatabase_GivesTheTablesTheCharacterSetAndLeavesTheDefaultOfTheDatabase()
    {
        // Arrange
        var database = server.Database("charset", MigratedStores.TablePrefix);
        await database.CreateEmptyAsync(Ct);
        await database.ExecuteAsync("ALTER DATABASE CHARACTER SET latin1 COLLATE latin1_swedish_ci", Ct);
        await using var provider = MigratedStores.Build(database.ConnectionString);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(
            [["latin1", "utf8mb4", "utf8mb4_bin", "utf8mb4", "varchar(36)"]],
            await database.RowsAsync(
                """
                SELECT s.DEFAULT_CHARACTER_SET_NAME, c.CHARACTER_SET_NAME, c.COLLATION_NAME, j.CHARACTER_SET_NAME, i.COLUMN_TYPE
                FROM information_schema.SCHEMATA s
                JOIN information_schema.COLUMNS c ON c.TABLE_SCHEMA = s.SCHEMA_NAME AND c.TABLE_NAME = 'MsgHatchInbox' AND c.COLUMN_NAME = 'TenantId'
                JOIN information_schema.COLUMNS j ON j.TABLE_SCHEMA = s.SCHEMA_NAME AND j.TABLE_NAME = 'MsgHatchInbox' AND j.COLUMN_NAME = 'Content'
                JOIN information_schema.COLUMNS i ON i.TABLE_SCHEMA = s.SCHEMA_NAME AND i.TABLE_NAME = 'MsgHatchInbox' AND i.COLUMN_NAME = 'Id'
                WHERE s.SCHEMA_NAME = DATABASE()
                """,
                Ct));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_SecondCall_ChangesNothing()
    {
        // Arrange
        var database = server.Database("twice", MigratedStores.TablePrefix);
        await using var provider = MigratedStores.Build(database.ConnectionString);
        await provider.MigrateMsgHatchAsync(ct: Ct);
        var schema = await MySqlSchema.OfAsync(database, Ct, _history);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(schema, await MySqlSchema.OfAsync(database, Ct, _history));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_OwnProviderSetup_MigratesItsOwnDatabaseAndTheStoresKeepTheirs()
    {
        // Arrange
        var stores = server.Database("own_stores", MigratedStores.TablePrefix);
        var migration = server.Database("own_migration", MigratedStores.TablePrefix);
        await stores.CreateEmptyAsync(Ct);
        await migration.CreateEmptyAsync(Ct);
        await using var provider = MigratedStores.Build(stores.ConnectionString);

        // Act
        await provider.MigrateMsgHatchAsync(o => o.UseMySql(migration.ConnectionString, MySqlTestDatabase.ServerVersion), Ct);

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(
            (6, 0, new MySqlConnectionStringBuilder(stores.ConnectionString).Database),
            ((await MySqlSchema.TablesOfAsync(migration, Ct)).Count, (await MySqlSchema.TablesOfAsync(stores, Ct)).Count,
                context.Database.GetDbConnection().Database));
    }

    [Fact]
    public async Task ScriptMsgHatchMigrationsAsync_EmptyDatabase_CreatesTheSchemaOfTheMigrationsAndASecondRunChangesNothing()
    {
        // Arrange
        var scripted = server.Database("script", MigratedStores.TablePrefix);
        var migrated = server.Database("script_migrated", MigratedStores.TablePrefix);
        await scripted.CreateEmptyAsync(Ct);
        await using var provider = MigratedStores.Build(scripted.ConnectionString);
        await using (var migrating = MigratedStores.Build(migrated.ConnectionString))
        {
            await migrating.MigrateMsgHatchAsync(ct: Ct);
        }

        var script = await provider.ScriptMsgHatchMigrationsAsync(Ct);

        // Act
        await scripted.ExecuteScriptAsync(script, Ct);
        var afterFirstRun = await MySqlSchema.OfAsync(scripted, Ct, _history);
        await scripted.ExecuteScriptAsync(script, Ct);

        // Assert
        Assert.Equal(await MySqlSchema.OfAsync(migrated, Ct, _history), afterFirstRun);
        Assert.Equal(afterFirstRun, await MySqlSchema.OfAsync(scripted, Ct, _history));
        Assert.DoesNotContain("ALTER DATABASE", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequireCurrentSchema_EachMigrationApplied_PassesTheStartup()
    {
        // Arrange
        var database = server.Database("require_applied", MigratedStores.TablePrefix);
        await using var provider = MigratedStores.Build(database.ConnectionString, static o => o.RequireCurrentSchema = true);
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task AddAsync_TargetIdLongerThanItsColumn_GivesValidationFailed()
    {
        // Arrange
        var database = MigratedStores.Of(server, "truncation");
        await database.ResetAsync(Ct);
        var store = new EntityFrameworkOutboxStore(MigratedStores.ContextsOf(database), new MsgHatchJson(TestContentTypes.ARegistry()));
        var message = TestMessages.AnOutgoingMessage(targetId: new string('u', 300));

        // Act
        var error = await store.For(TenantKey.None).AddAsync([message], Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, error?.Type);
    }

    [Theory]
    [InlineData(null, "longtext")]
    [InlineData("json", "json")]
    public async Task MigrateMsgHatchAsync_JsonColumnTypeOfTheOption_IsTheTypeOfEachJsonColumnWithNoPendingModelChange(string? option, string expected)
    {
        // Arrange
        var database = server.Database($"json_{expected}", MigratedStores.TablePrefix);
        await using var provider = MigratedStores.Build(database.ConnectionString, o => o.JsonColumnType = option);
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(
            [["MsgHatchInbox.Content", expected], ["MsgHatchInbox.LastError", "longtext"], ["MsgHatchMessageTypeEntry.DisabledChannels", expected],
                ["MsgHatchOutbox.Content", expected], ["MsgHatchOutbox.LastError", "longtext"], ["MsgHatchOutbox.Trace", expected], ["MsgHatchOutbox.Tries", expected]],
            await database.RowsAsync(
                """
                SELECT CONCAT(TABLE_NAME, '.', COLUMN_NAME), DATA_TYPE FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND COLUMN_NAME IN ('Content', 'Trace', 'Tries', 'DisabledChannels', 'LastError')
                ORDER BY CAST(CONCAT(TABLE_NAME, '.', COLUMN_NAME) AS BINARY)
                """,
                Ct));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_CollationOfTheDialect_KeepsTwoTenantsThatDifferOnlyInCaseApart()
    {
        // Arrange
        var database = server.Database("collation", MigratedStores.TablePrefix);
        await using var provider = MigratedStores.Build(database.ConnectionString);
        await provider.MigrateMsgHatchAsync(ct: Ct);
        var store = new EntityFrameworkInboxStore(
            provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>(), new MsgHatchJson(TestContentTypes.ARegistry()));
        var lower = TestMessages.AReceivedMessage(new TestEvent("text"), new TenantId("t1"));
        var upper = TestMessages.AReceivedMessage(new TestEvent("text"), new TenantId("T1"), delivery: lower.Delivery);

        // Act
        await store.For(TenantKey.For(lower.Tenant)).AddAsync(lower, Ct);
        await store.For(TenantKey.For(upper.Tenant)).AddAsync(upper, Ct);

        // Assert
        Assert.Equal(
            [["t1", "utf8mb4_bin"], ["T1", "utf8mb4_bin"]],
            await database.RowsAsync(
                """
                SELECT TenantId, (SELECT COLLATION_NAME FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'MsgHatchInbox' AND COLUMN_NAME = 'TenantId')
                FROM MsgHatchInbox ORDER BY Sequence
                """,
                Ct));
    }
}
