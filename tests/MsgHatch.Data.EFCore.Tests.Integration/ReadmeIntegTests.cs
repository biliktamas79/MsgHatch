using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.Tests.Integration;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). Only the test of
/// the migration step opens a connection, to a database of the fixture.
/// </summary>
public sealed class ReadmeIntegTests(SqlServerFixture server)
{
    private const string _testConnectionString = "Data Source=msghatch.db";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(EntityFrameworkStoreOptions));

    [Fact]
    public async Task UseEntityFrameworkStores_SettingsOfTheReadme_ResolvesTheOutboxStore()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        var connectionString = _testConnectionString;

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseEntityFrameworkStores(o => o.UseSqlite(connectionString));
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IOutboxStore>());
        Assert.Equal(
            ("msghatch", true),
            (provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value.Schema,
                provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value.RequireCurrentSchema));
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SchemaInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _testConnectionString;

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseEntityFrameworkStores(
                o => o.UseSqlite(connectionString),
                o => o.Schema = "messaging");
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("messaging", provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value.Schema);
    }

    [Fact]
    public async Task UseEntityFrameworkSqlServerStores_SetupOfTheReadme_GivesTheContextsTheDialectOfSqlServer()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = server.Database("readme").ConnectionString;

        // Act
        #region readme:setup-sqlserver
        services.AddMsgHatch()
            .UseEntityFrameworkSqlServerStores(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_StepOfTheReadme_CreatesTheTablesOfTheStores()
    {
        // Arrange
        var database = server.Database("readme_migrate");
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseEntityFrameworkSqlServerStores(database.ConnectionString);
        await using var serviceProvider = services.Build();
        var cancellationToken = Ct;

        // Act
        #region readme:migrate
        await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
        #endregion

        // Assert
        Assert.Equal(6, (await database.RowsAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES", Ct)).Count);
    }
}
