using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.MySql.Tests.Integration;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The migration
/// examples run on databases of the fixture.
/// </summary>
public sealed class ReadmeIntegTests(MySqlFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task AddExceptionClassifier_ClassifierOfTheReadme_IsRegisteredForTheStores()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = server.Database("readme").ConnectionString;

        // Act
        #region readme:classifier
        services.AddMsgHatch()
            .UseEntityFrameworkMySqlStores(connectionString)
            .AddExceptionClassifier<MsgHatchDbContext, MySqlReadOnlyClassifier>();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.IsType<MySqlReadOnlyClassifier>(Assert.Single(provider.GetServices<IExceptionClassifier<MsgHatchDbContext>>()));
    }

    [Fact]
    public async Task UseEntityFrameworkMySqlStores_SettingsOfTheReadme_RequiresTheCurrentSchema()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        var connectionString = server.Database("readme").ConnectionString;

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseEntityFrameworkMySqlStores(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.True(provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value.RequireCurrentSchema);
    }

    [Fact]
    public async Task UseEntityFrameworkMySqlStores_OptionsInCode_UsesTheJsonTypeOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = server.Database("readme").ConnectionString;

        // Act
        #region readme:setup-options
        services.AddMsgHatch()
            .UseEntityFrameworkMySqlStores(
                connectionString,
                o => o.JsonColumnType = "json");
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal("json", context.Model.GetEntityTypes().Single(static type => type.GetTableName() == "MsgHatchInbox").FindProperty("Content")!.GetColumnType());
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_StepsOfTheReadme_MigrateTheDatabaseOfEachStepAndScriptTheMigrations()
    {
        // Arrange
        var database = server.Database("readme_migrate", MigratedStores.TablePrefix);
        var migrationDatabase = server.Database("readme_migrate_own", MigratedStores.TablePrefix);
        var migrationConnectionString = migrationDatabase.ConnectionString;
        await using var serviceProvider = MigratedStores.Build(database.ConnectionString);
        var cancellationToken = Ct;

        // Act
        #region readme:migrate
        await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
        #endregion
        #region readme:migrate-own-setup
        await serviceProvider.MigrateMsgHatchAsync(
            o => o.UseMySql(migrationConnectionString, ServerVersion.Parse("8.4.0-mysql")), cancellationToken);
        #endregion
        #region readme:script
        var script = await serviceProvider.ScriptMsgHatchMigrationsAsync(cancellationToken);
        #endregion

        // Assert
        Assert.Equal(
            (6, 6, true),
            ((await MySqlSchema.TablesOfAsync(database, Ct)).Count, (await MySqlSchema.TablesOfAsync(migrationDatabase, Ct)).Count,
                script.Contains("__MsgHatchStoreMigrations", StringComparison.Ordinal)));
    }
}
