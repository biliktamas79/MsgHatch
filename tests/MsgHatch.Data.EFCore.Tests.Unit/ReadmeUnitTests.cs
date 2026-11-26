using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;
using NSubstitute;

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
        var services = EntityFrameworkTestHost.CreateServices();
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
        var services = EntityFrameworkTestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
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
        var services = EntityFrameworkTestHost.CreateServices();
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
        var services = EntityFrameworkTestHost.CreateServices();
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
        var services = EntityFrameworkTestHost.CreateServices();
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
        var services = EntityFrameworkTestHost.CreateServices();
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

    [Fact]
    public async Task AcceptAsync_ExampleOfTheReadme_StoresTheOrderAndTheInboxRowInOneSave()
    {
        // Arrange
        var database = new SqliteTestDatabase();
        var services = EntityFrameworkTestHost.CreateServices();
        var connectionString = database.ConnectionString;
        #region readme:host-setup
        services.AddDbContext<TestHostContext>(o => o.UseSqlite(connectionString));
        services.AddMsgHatch()
            .UseEntityFrameworkStores<TestHostContext>(o => o.UseSqlite(connectionString));
        #endregion
        services.AddMsgHatch().AddMessage(
            "TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m.WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>()));
        await using var provider = services.Build();
        await database.ResetAsync(provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>(), Ct);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestHostContext>();
        await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(Ct);
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var order = new TestOrder { Text = "order 1001", CreatedAtUtc = TestHost.Start.UtcDateTime };
        var eventId = Guid.NewGuid();
        var ct = Ct;

        // Act
        #region readme:host-accept
        db.Orders.Add(order);
        await notifications.AcceptAsync(new TestEvent("order 1001"), new MessageContext(eventId), ct);
        await db.SaveChangesAsync(ct);
        #endregion

        // Assert
        Assert.Equal([[order.Text, $"{eventId:D}"]], database.Rows("SELECT o.text, i.Id FROM Orders o CROSS JOIN Inbox i"));
    }

    [Fact]
    public async Task MapMsgHatchInbox_ContextOfTheReadme_MapsTheInboxWithNoMigration()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ReadmeAppContext>().UseSqlite(_testConnectionString);
        options.IgnoreManyServiceProvidersWarning();

        // Act
        await using var context = new ReadmeAppContext(options.Options);

        // Assert
        Assert.True(TableDescriptions.TableOf(context, "Inbox").IsExcludedFromMigrations);
    }

    private sealed class ReadmeAppContext(DbContextOptions<ReadmeAppContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            #region readme:host-model
            modelBuilder.MapMsgHatchInbox(this);
            #endregion
        }
    }
}
