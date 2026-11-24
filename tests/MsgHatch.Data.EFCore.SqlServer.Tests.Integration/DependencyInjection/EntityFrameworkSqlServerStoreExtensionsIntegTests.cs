using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkSqlServerStores</c> and the migration step on SQL Server (D318, D320): the migrations of the
/// package create the schema of the options, the model has no pending change against them, and the script and a migration
/// with its own provider setup give the same schema. Each test has its own database.
/// </summary>
public sealed class EntityFrameworkSqlServerStoreExtensionsIntegTests(SqlServerFixture server)
{
    public static TheoryData<string, string?> ModelRows => new()
    {
        { "msghatch", null },
        { "other", "Latin1_General_100_CS_AS" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkSqlServerStores_Alone_PassesTheStartupValidation()
    {
        // Arrange
        await using var provider = MigratedStores.Build(server.Database("setup").ConnectionString);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_EmptyDatabaseAndASchemaOfTheOption_CreatesTheTablesAndTheHistoryTableInThatSchema()
    {
        // Arrange
        var database = server.Database("schema");
        await using var provider = MigratedStores.Build(database.ConnectionString, static o => o.Schema = "other");

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(
            [["other", "__MsgHatchMigrations"], ["other", "MsgHatchChannelEntry"], ["other", "MsgHatchChannelState"], ["other", "MsgHatchInbox"],
                ["other", "MsgHatchMessageTypeEntry"], ["other", "MsgHatchOutbox"]],
            await database.RowsAsync("SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_SCHEMA, TABLE_NAME", Ct));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_SecondCall_ChangesNothing()
    {
        // Arrange
        var database = server.Database("twice");
        await using var provider = MigratedStores.Build(database.ConnectionString);
        await provider.MigrateMsgHatchAsync(ct: Ct);
        var schema = await SchemaOfAsync(database);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(schema, await SchemaOfAsync(database));
    }

    [Theory]
    [MemberData(nameof(ModelRows))]
    public async Task HasPendingModelChanges_SchemaAndCollationOfTheOptions_HasNoChangeAgainstTheMigrationsOfThePackage(string schema, string? collation)
    {
        // Arrange
        await using var provider = MigratedStores.Build(server.Database("model").ConnectionString, o => (o.Schema, o.TextCollation) = (schema, collation));
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var hasChanges = context.Database.HasPendingModelChanges();

        // Assert
        Assert.False(hasChanges);
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_OwnProviderSetup_MigratesItsOwnDatabaseAndTheStoresKeepTheirs()
    {
        // Arrange
        var stores = server.Database("own_stores");
        var migration = server.Database("own_migration");
        await stores.CreateEmptyAsync(Ct);
        await using var provider = MigratedStores.Build(stores.ConnectionString);

        // Act
        await provider.MigrateMsgHatchAsync(o => o.UseSqlServer(migration.ConnectionString), Ct);

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(
            (6, 0, new SqlConnectionStringBuilder(stores.ConnectionString).InitialCatalog),
            ((await migration.RowsAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES", Ct)).Count,
                (await stores.RowsAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES", Ct)).Count, context.Database.GetDbConnection().Database));
    }

    [Fact]
    public async Task ScriptMsgHatchMigrationsAsync_EmptyDatabase_CreatesTheSchemaOfTheMigrationsAndASecondRunChangesNothing()
    {
        // Arrange
        var scripted = server.Database("script");
        var migrated = server.Database("script_migrated");
        await scripted.CreateEmptyAsync(Ct);
        await using var provider = MigratedStores.Build(scripted.ConnectionString);
        await using (var migrating = MigratedStores.Build(migrated.ConnectionString))
        {
            await migrating.MigrateMsgHatchAsync(ct: Ct);
        }

        var script = await provider.ScriptMsgHatchMigrationsAsync(Ct);

        // Act
        await SqlServerSchema.RunScriptAsync(scripted, script, Ct);
        var afterFirstRun = await SchemaOfAsync(scripted);
        await SqlServerSchema.RunScriptAsync(scripted, script, Ct);

        // Assert
        Assert.Equal(await SchemaOfAsync(migrated), afterFirstRun);
        Assert.Equal(afterFirstRun, await SchemaOfAsync(scripted));
    }

    [Fact]
    public async Task RequireCurrentSchema_MigrationThatIsNotApplied_FailsTheStartupWithTheContextAndTheMigration()
    {
        // Arrange
        var database = server.Database("require_pending");
        await using var provider = MigratedStores.Build(database.ConnectionString, static o => o.RequireCurrentSchema = true);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true),
            (failure.Contains(nameof(MsgHatchDbContext), StringComparison.Ordinal), failure.Contains("_Initial", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RequireCurrentSchema_EachMigrationApplied_PassesTheStartup()
    {
        // Arrange
        var database = server.Database("require_applied");
        await using var provider = MigratedStores.Build(database.ConnectionString, static o => o.RequireCurrentSchema = true);
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_CaseSensitiveCollationOfTheOption_KeepsTwoTenantsThatDifferOnlyInCaseApart()
    {
        // Arrange
        var database = server.Database("collation");
        await using var provider = MigratedStores.Build(database.ConnectionString, static o => o.TextCollation = "Latin1_General_100_CS_AS");
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
            [["t1", "Latin1_General_100_CS_AS"], ["T1", "Latin1_General_100_CS_AS"]],
            await database.RowsAsync(
                """
                SELECT TenantId, (SELECT COLLATION_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MsgHatchInbox' AND COLUMN_NAME = 'TenantId')
                FROM [msghatch].[MsgHatchInbox] ORDER BY Sequence
                """,
                Ct));
    }

    private static Task<List<string?[]>> SchemaOfAsync(TestDatabase database) =>
        SqlServerSchema.OfAsync(database, Ct, "[msghatch].[__MsgHatchMigrations]");
}
