using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.Tests.Unit;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The check reads the
/// sources of this project and of the integration test project of the package, where the migration examples run on a
/// server (D330). No test of this class opens a connection.
/// </summary>
public sealed class ReadmeUnitTests
{
    private const string _testConnectionString = "Data Source=msghatch.db";

    private const string _postgreSqlConnectionString = "Host=localhost;Database=msghatch";

    private const string _mySqlConnectionString = "Server=localhost;Database=msghatch";

    private const string _connectionString = "Server=127.0.0.1,1;Database=readme";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(EntityFrameworkStoreOptions));

    [Fact]
    public async Task AddExceptionClassifier_ClassifierOfTheReadme_RejectsAReadOnlyDatabase()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _testConnectionString;

        // Act
        #region readme:classifier
        services.AddMsgHatch()
            .UseEntityFrameworkStores(o => o.UseSqlite(connectionString))
            .AddExceptionClassifier<MsgHatchDbContext, SqliteReadOnlyClassifier>();
        #endregion
        await using var provider = services.Build();

        // Assert
        var classifier = Assert.Single(provider.GetServices<IExceptionClassifier<MsgHatchDbContext>>());
        Assert.Equal(
            (Errors.Rejected, (ErrorType?)null),
            (classifier.Classify(new DbUpdateException("update", new SqliteException("read-only", 8))), classifier.Classify(new SqliteException("busy", 5))));
    }

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
        var connectionString = _connectionString;

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
    public async Task UseEntityFrameworkPostgreSqlStores_SetupOfTheReadme_GivesTheContextsTheDialectOfPostgreSql()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _postgreSqlConnectionString;

        // Act
        #region readme:setup-postgresql
        services.AddMsgHatch()
            .UseEntityFrameworkPostgreSqlStores(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    [Fact]
    public async Task UseEntityFrameworkMySqlStores_SetupOfTheReadme_GivesTheContextsTheTablesWithThePrefix()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _mySqlConnectionString;

        // Act
        #region readme:setup-mysql
        services.AddMsgHatch()
            .UseEntityFrameworkMySqlStores(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(
            ("Microting.EntityFrameworkCore.MySql", "MsgHatchOutbox"),
            (context.Database.ProviderName, context.Model.GetEntityTypes().Single(static type => type.ClrType.Name == "OutboxRow").GetTableName()));
    }
}
