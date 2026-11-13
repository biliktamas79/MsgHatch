using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.EFCore.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkStores</c> alone registers all parts of the package (section 4.7, D60), and its startup check
/// rejects a provider with no SQL statements of the stores (D175).
/// </summary>
public sealed class EntityFrameworkStoreExtensionsIntegTests(SqlServerFixture server)
{
    private const string _schemaKey = "MsgHatch:EntityFramework:Schema";

    private const string _sqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Now => TestHost.Start.UtcDateTime;

    private TestDatabase Database => server.Database("setup");

    [Fact]
    public async Task UseEntityFrameworkStores_Alone_ResolvesEachStoreAsItsLoggingWrapperAroundTheEntityFrameworkStores()
    {
        // Arrange
        await Database.ResetAsync(Ct);
        await using var provider = Build(o => o.UseSqlServer(Database.ConnectionString), enableLogging: true);
        var received = TestMessages.AReceivedMessage(new TestEvent("text"));
        var outgoing = TestMessages.AnOutgoingMessage(channel: TestChannel.Id);
        await provider.GetRequiredService<IInboxStore>().AddAsync(received, Ct);
        await provider.GetRequiredService<IOutboxStore>().AddAsync([outgoing], Ct);

        // Act
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(
            [1200, 1202, 1300, 1304],
            provider.LogRecords().Where(static record => record.Category?.StartsWith("MsgHatch.", StringComparison.Ordinal) == true)
                .Select(static record => record.Id.Id).Order());
        Assert.Equal(
            [received.Delivery.Id, outgoing.Delivery.Id],
            [Assert.Single(inbox.Value.Messages).Delivery.Id, Assert.Single(outbox.Value.Messages).Delivery.Id]);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_LoggingDisabled_TheStoresAreTheOneInstanceOfEachEntityFrameworkStore()
    {
        // Arrange
        await using var provider = Build(o => o.UseSqlServer(Database.ConnectionString), enableLogging: false);

        // Act
        object[] stores =
        [
            provider.GetRequiredService<IInboxDispatchStore>(), provider.GetRequiredService<IOutboxDispatchStore>(),
            provider.GetRequiredService<IRegistrySettingsStore>(),
        ];

        // Assert
        Assert.Equal(
            [provider.GetRequiredService<EntityFrameworkInboxStore>(), provider.GetRequiredService<EntityFrameworkOutboxStore>(),
                provider.GetRequiredService<EntityFrameworkRegistrySettingsStore>()],
            stores);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SchemaFromCode_KeepsTheTablesAndTheRowsInThatSchema()
    {
        // Arrange
        var database = server.Database("schema");
        await using var provider = Build(o => o.UseSqlServer(database.ConnectionString), enableLogging: false, o => o.Schema = "other");
        await using (var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct))
        {
            await context.Database.EnsureCreatedAsync(Ct);
        }

        // Act
        var error = await provider.GetRequiredService<IInboxStore>().AddAsync(TestMessages.AReceivedMessage(new TestEvent("text")), Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(
            [["other", "MsgHatchInbox", "1"]],
            await database.RowsAsync(
                "SELECT TABLE_SCHEMA, TABLE_NAME, (SELECT COUNT(*) FROM [other].[MsgHatchInbox]) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MsgHatchInbox'",
                Ct));
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SchemaInTheSection_ReachesTheOptions()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [_schemaKey] = "from_section" });
        services.AddMsgHatch().UseEntityFrameworkStores(o => o.UseSqlServer(Database.ConnectionString));
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value;

        // Assert
        Assert.Equal("from_section", options.Schema);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SchemaFromCode_WinsOverTheSection()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [_schemaKey] = "from_section" });
        services.AddMsgHatch().UseEntityFrameworkStores(o => o.UseSqlServer(Database.ConnectionString), o => o.Schema = "from_code");
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value;

        // Assert
        Assert.Equal("from_code", options.Schema);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SchemaThatIsNotAnIdentifier_FailsTheValidationOnSchema()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [_schemaKey] = "msg-hatch" });
        services.AddMsgHatch().UseEntityFrameworkStores(o => o.UseSqlServer(Database.ConnectionString));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(() => provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal("Schema", failure[..failure.IndexOf(':', StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_ProviderWithNoStatements_FailsTheStartupValidationAndNamesTheProvider()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkStores(static o => o.UseSqlite("Data Source=:memory:"));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Contains(_sqliteProvider, failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaimDueAsync_ProviderWithNoStatements_GivesNotFoundForTheInboxAndTheOutbox()
    {
        // Arrange
        var contexts = new PooledDbContextFactory<MsgHatchDbContext>(
            new DbContextOptionsBuilder<MsgHatchDbContext>().UseSqlite("Data Source=:memory:").Options);
        var json = new MsgHatchJson(TestContentTypes.ARegistry());

        // Act
        var inbox = await new EntityFrameworkInboxStore(contexts, json).ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await new EntityFrameworkOutboxStore(contexts, json).ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal([Errors.NotFound, Errors.NotFound], [inbox.Error?.Type, outbox.Error?.Type]);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_AfterUseInMemoryStores_NamesBothSetupMethodsForEachStoreInterface()
    {
        // Arrange
        const string names = "UseInMemoryStores and UseEntityFrameworkStores select an implementation of ";
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseEntityFrameworkStores(o => o.UseSqlServer(Database.ConnectionString));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            ["IInboxDispatchStore", "IInboxStore", "IOutboxDispatchStore", "IOutboxStore", "IRegistrySettingsStore"],
            Assert.IsType<OptionsValidationException>(exception).Failures
                .Where(static failure => failure.StartsWith(names, StringComparison.Ordinal))
                .Select(static failure => failure[names.Length..^1])
                .Order(StringComparer.Ordinal));
    }

    private static ServiceProvider Build(
        Action<DbContextOptionsBuilder> configure, bool enableLogging, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddTestChannel()
            .UseEntityFrameworkStores(configure, configureOptions)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>())
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        return services.Build();
    }
}
