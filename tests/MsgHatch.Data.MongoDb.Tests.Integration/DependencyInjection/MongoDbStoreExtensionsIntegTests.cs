using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.MongoDb.Tests.Integration.DependencyInjection;

/// <summary><c>UseMongoDbStores</c> alone registers all parts of the package (section 4.7, D60).</summary>
public sealed class MongoDbStoreExtensionsIntegTests(MongoDbFixture server)
{
    private const string _connectionStringKey = "MsgHatch:MongoDb:ConnectionString";

    private const string _databaseNameKey = "MsgHatch:MongoDb:DatabaseName";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Now => TestHost.Start.UtcDateTime;

    private TestDatabase Database => server.Database("setup");

    [Fact]
    public async Task UseMongoDbStores_Alone_ResolvesEachStoreAsItsLoggingWrapperAroundTheMongoDbStores()
    {
        // Arrange
        await Database.ResetAsync(Ct);
        await using var provider = Build(enableLogging: true);
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
    public async Task UseMongoDbStores_LoggingDisabled_TheStoresAreTheOneInstanceOfEachMongoDbStore()
    {
        // Arrange
        await using var provider = Build(enableLogging: false);

        // Act
        object[] stores =
        [
            provider.GetRequiredService<IInboxDispatchStore>(), provider.GetRequiredService<IOutboxDispatchStore>(),
            provider.GetRequiredService<IRegistrySettingsStore>(),
        ];

        // Assert
        Assert.Equal(
            [provider.GetRequiredService<MongoDbInboxStore>(), provider.GetRequiredService<MongoDbOutboxStore>(),
                provider.GetRequiredService<MongoDbRegistrySettingsStore>()],
            stores);
    }

    [Fact]
    public async Task UseMongoDbStores_DatabaseNameFromCode_KeepsTheDocumentsInThatDatabase()
    {
        // Arrange
        var database = server.Database("name");
        await database.ResetAsync(Ct);
        await using var provider = Build(enableLogging: false, o => o.DatabaseName = database.Database.DatabaseNamespace.DatabaseName);

        // Act
        var error = await provider.GetRequiredService<IInboxStore>().AddAsync(TestMessages.AReceivedMessage(new TestEvent("text")), Ct);

        // Assert
        Assert.Null(error);
        Assert.Single(await database.DocumentsAsync(MongoDbInboxStore.CollectionName, Ct));
    }

    [Fact]
    public async Task UseMongoDbStores_DatabaseNameInTheSection_ReachesTheOptions()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: Section(databaseName: "from_section"));
        services.AddMsgHatch().UseMongoDbStores();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value;

        // Assert
        Assert.Equal(("from_section", server.ConnectionString), (options.DatabaseName, options.ConnectionString));
    }

    [Fact]
    public async Task UseMongoDbStores_DatabaseNameFromCode_WinsOverTheSection()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: Section(databaseName: "from_section"));
        services.AddMsgHatch().UseMongoDbStores(static o => o.DatabaseName = "from_code");
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value;

        // Assert
        Assert.Equal("from_code", options.DatabaseName);
    }

    [Fact]
    public async Task UseMongoDbStores_DatabaseNameWithADot_FailsTheValidationOnDatabaseName()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: Section(databaseName: "msg.hatch"));
        services.AddMsgHatch().UseMongoDbStores();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(() => provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal("DatabaseName", failure[..failure.IndexOf(':', StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task UseMongoDbStores_ClientOfTheHost_StaysTheClientOfTheStores()
    {
        // Arrange
        var client = Substitute.For<IMongoClient>();
        var services = TestHost.CreateServices(configuration: Section(databaseName: "msghatch"));
        services.AddSingleton(client);
        services.AddMsgHatch().UseMongoDbStores();
        await using var provider = services.Build();

        // Act
        var resolved = provider.GetRequiredService<IMongoClient>();

        // Assert
        Assert.Same(client, resolved);
    }

    [Fact]
    public async Task UseMongoDbStores_AfterUseInMemoryStores_NamesBothSetupMethodsForEachStoreInterface()
    {
        // Arrange
        const string names = "UseInMemoryStores and UseMongoDbStores select an implementation of ";
        var services = TestHost.CreateServices(configuration: Section(databaseName: "msghatch"));
        services.AddMsgHatch().UseInMemoryStores().UseMongoDbStores();
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

    private Dictionary<string, string?> Section(string databaseName) =>
        new() { [_connectionStringKey] = server.ConnectionString, [_databaseNameKey] = databaseName };

    private ServiceProvider Build(bool enableLogging, Action<MongoDbStoreOptions>? configure = null)
    {
        var services = TestHost.CreateServices(configuration: Section(Database.Database.DatabaseNamespace.DatabaseName));
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddTestChannel()
            .UseMongoDbStores(configure)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>())
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        return services.Build();
    }
}
