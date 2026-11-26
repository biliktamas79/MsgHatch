using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Subscriptions.Data.EFCore.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Unit;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The check reads the
/// sources of this project and of the integration test project of the package, where the migration examples run on a
/// server (D330). No test of this class opens a connection.
/// </summary>
public sealed class ReadmeUnitTests
{
    private const string _testConnectionString = "Data Source=msghatch.db";

    private const string _postgreSqlConnectionString = "Host=localhost;Database=msghatch";

    private const string _connectionString = "Server=127.0.0.1,1;Database=readme";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

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
            .UseSubscriptions()
            .UseEntityFrameworkSubscriptionStore(o => o.UseSqlite(connectionString))
            .AddExceptionClassifier<MsgHatchSubscriptionDbContext, SqliteReadOnlySubscriptionsClassifier>();
        #endregion
        await using var provider = services.Build();

        // Assert
        var classifier = Assert.Single(provider.GetServices<IExceptionClassifier<MsgHatchSubscriptionDbContext>>());
        Assert.Equal(
            (Errors.Rejected, 0),
            (classifier.Classify(new DbUpdateException("update", new SqliteException("read-only", 8))),
                provider.GetServices<IExceptionClassifier<MsgHatchDbContext>>().Count()));
    }

    [Fact]
    public async Task UseEntityFrameworkSubscriptionStore_SettingsOfTheReadme_SharesTheSchemaOfTheStores()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        var connectionString = _testConnectionString;

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseEntityFrameworkStores(o => o.UseSqlite(connectionString))
            .UseSubscriptions()
            .UseEntityFrameworkSubscriptionStore(o => o.UseSqlite(connectionString));
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("msghatch", provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value.Schema);
    }

    [Fact]
    public async Task UseEntityFrameworkSqlServerSubscriptionStore_SetupOfTheReadme_GivesTheContextTheCollationOfTheDialect()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _connectionString;

        // Act
        #region readme:setup-sqlserver
        services.AddMsgHatch()
            .UseEntityFrameworkSqlServerStores(connectionString)
            .UseSubscriptions()
            .UseEntityFrameworkSqlServerSubscriptionStore(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
    }

    [Fact]
    public async Task UseEntityFrameworkPostgreSqlSubscriptionStore_SetupOfTheReadme_GivesTheContextTheProviderOfPostgreSql()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _postgreSqlConnectionString;

        // Act
        #region readme:setup-postgresql
        services.AddMsgHatch()
            .UseEntityFrameworkPostgreSqlStores(connectionString)
            .UseSubscriptions()
            .UseEntityFrameworkPostgreSqlSubscriptionStore(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }
}
