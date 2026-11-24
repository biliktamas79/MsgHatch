using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using Npgsql;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkPostgreSqlStores</c> and the migration step on PostgreSQL (D318, D320): the migrations of the
/// package create the schema of the options, the model has no pending change against them, and the script and a migration
/// with its own provider setup give the same schema. Each test has its own database.
/// </summary>
public sealed class EntityFrameworkPostgreSqlStoreExtensionsIntegTests(PostgreSqlFixture server)
{
    private const string _history = "\"msghatch\".\"__StoreMigrations\"";

    public static TheoryData<string, string?> ModelRows => new()
    {
        { "msghatch", null },
        { "other", "POSIX" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkPostgreSqlStores_Alone_PassesTheStartupValidation()
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
            [["other", "ChannelEntry"], ["other", "ChannelState"], ["other", "Inbox"], ["other", "MessageTypeEntry"],
                ["other", "Outbox"], ["other", "__StoreMigrations"]],
            await PostgreSqlSchema.TablesOfAsync(database, Ct));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_SecondCall_ChangesNothing()
    {
        // Arrange
        var database = server.Database("twice");
        await using var provider = MigratedStores.Build(database.ConnectionString);
        await provider.MigrateMsgHatchAsync(ct: Ct);
        var schema = await PostgreSqlSchema.OfAsync(database, Ct, _history);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(schema, await PostgreSqlSchema.OfAsync(database, Ct, _history));
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
        await provider.MigrateMsgHatchAsync(o => o.UseNpgsql(migration.ConnectionString), Ct);

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(
            (6, 0, new NpgsqlConnectionStringBuilder(stores.ConnectionString).Database),
            ((await PostgreSqlSchema.TablesOfAsync(migration, Ct)).Count, (await PostgreSqlSchema.TablesOfAsync(stores, Ct)).Count,
                context.Database.GetDbConnection().Database));
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
        await scripted.ExecuteAsync(script, Ct);
        var afterFirstRun = await PostgreSqlSchema.OfAsync(scripted, Ct, _history);
        await scripted.ExecuteAsync(script, Ct);

        // Assert
        Assert.Equal(await PostgreSqlSchema.OfAsync(migrated, Ct, _history), afterFirstRun);
        Assert.Equal(afterFirstRun, await PostgreSqlSchema.OfAsync(scripted, Ct, _history));
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
    public async Task Stores_DatabaseWithNoMigrations_GiveValidationFailed()
    {
        // Arrange
        var database = server.Database("unmigrated");
        await database.CreateEmptyAsync(Ct);
        await using var provider = MigratedStores.Build(database.ConnectionString);
        var contexts = provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>();
        var json = new MsgHatchJson(TestContentTypes.ARegistry());
        var now = TestHost.Start.UtcDateTime;

        // Act
        var inbox = await new EntityFrameworkInboxStore(contexts, json).ClaimDueAsync(10, now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await new EntityFrameworkOutboxStore(contexts, json).ClaimDueAsync(TestChannel.Id, 10, now, TimeSpan.FromSeconds(30), Ct);
        var registry = await new EntityFrameworkRegistrySettingsStore(contexts).GetEntriesAsync(Ct);

        // Assert
        Assert.Equal([Errors.ValidationFailed, Errors.ValidationFailed, Errors.ValidationFailed], [inbox.Error?.Type, outbox.Error?.Type, registry.Error?.Type]);
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

    [Fact]
    public async Task MigrateMsgHatchAsync_CollationOfTheDialect_KeepsTwoTenantsThatDifferOnlyInCaseApart()
    {
        // Arrange
        var database = server.Database("collation");
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
            [["t1", "C"], ["T1", "C"]],
            await database.RowsAsync(
                """
                SELECT "TenantId", (SELECT collation_name FROM information_schema.columns WHERE table_name = 'Inbox' AND column_name = 'TenantId')
                FROM "msghatch"."Inbox" ORDER BY "Sequence"
                """,
                Ct));
    }
}
