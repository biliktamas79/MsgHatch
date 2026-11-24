using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Integration;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The migration
/// examples run on databases of the fixture.
/// </summary>
public sealed class ReadmeIntegTests(SqlServerFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task UseEntityFrameworkSqlServerStores_SettingsOfTheReadme_RequiresTheCurrentSchema()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        var connectionString = server.Database("readme").ConnectionString;

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseEntityFrameworkSqlServerStores(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.True(provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value.RequireCurrentSchema);
    }

    [Fact]
    public async Task UseEntityFrameworkSqlServerStores_OptionsInCode_UsesTheSchemaAndTheRetriesOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = server.Database("readme").ConnectionString;

        // Act
        #region readme:setup-options
        services.AddMsgHatch()
            .UseEntityFrameworkSqlServerStores(
                connectionString,
                o => o.Schema = "messaging",
                sql => sql.EnableRetryOnFailure());
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(("messaging", true), (context.Model.GetDefaultSchema(), context.Database.CreateExecutionStrategy().RetriesOnFailure));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_StepsOfTheReadme_MigrateTheDatabaseOfEachStepAndScriptTheMigrations()
    {
        // Arrange
        var database = server.Database("readme_migrate");
        var migrationDatabase = server.Database("readme_migrate_own");
        var migrationConnectionString = migrationDatabase.ConnectionString;
        await using var serviceProvider = MigratedStores.Build(database.ConnectionString);
        var cancellationToken = Ct;

        // Act
        #region readme:migrate
        await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
        #endregion
        #region readme:migrate-own-setup
        await serviceProvider.MigrateMsgHatchAsync(o => o.UseSqlServer(migrationConnectionString), cancellationToken);
        #endregion
        #region readme:script
        var script = await serviceProvider.ScriptMsgHatchMigrationsAsync(cancellationToken);
        #endregion

        // Assert
        Assert.Equal(
            (6, 6, true),
            ((await database.RowsAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES", Ct)).Count,
                (await migrationDatabase.RowsAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES", Ct)).Count,
                script.Contains("__MsgHatchMigrations", StringComparison.Ordinal)));
    }
}
