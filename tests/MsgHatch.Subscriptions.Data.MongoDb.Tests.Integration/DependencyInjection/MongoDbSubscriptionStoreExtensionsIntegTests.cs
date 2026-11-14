using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.MongoDb.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseMongoDbSubscriptionStore</c> registers the MongoDB subscription store of each tenant key, with its logging
/// wrapper, on the shared <c>MongoDbStoreOptions</c> (D59). Each test has its own database.
/// </summary>
public sealed class MongoDbSubscriptionStoreExtensionsIntegTests(MongoDbFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseMongoDbSubscriptionStore_Alone_ResolvesTheStoreOfAKeyAsItsLoggingWrapperOnTheDatabase()
    {
        // Arrange
        var database = server.Database("setup");
        var services = TestHost.CreateServices(configuration: Section(database));
        services.AddMsgHatch().AddScenarioMessages().UseMongoDbSubscriptionStore();
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredKeyedService<ISubscriptionStore>(TenantKey.For(new TenantId("t1"))).AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(1106, Assert.Single(MsgHatchLogRecords(provider)).Id.Id);
        Assert.Equal(
            ["t1", Scenario.S6.Id.ToString("D")],
            (await Documents(database).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync(Ct)) is var document
                ? [document["tenant"], document["_id"]] : Array.Empty<BsonValue>());
    }

    [Fact]
    public async Task UseMongoDbSubscriptionStore_AfterUseMongoDbStoresWithADatabaseNameFromCode_KeepsTheDocumentsInThatDatabase()
    {
        // Arrange
        var shared = server.Database("shared");
        var services = TestHost.CreateServices(configuration: Section(server.Database("setup")));
        services.AddMsgHatch()
            .AddScenarioMessages()
            .UseMongoDbStores(o => o.DatabaseName = shared.Database.DatabaseNamespace.DatabaseName)
            .UseMongoDbSubscriptionStore();
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<ISubscriptionStore>().AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(1, await Documents(shared).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: Ct));
    }

    [Fact]
    public async Task UseMongoDbSubscriptionStore_ValueInCodeAlsoInUseMongoDbStores_FailsTheStartupValidationAndNamesBothMethods()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: Section(server.Database("setup")));
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseMongoDbStores(static o => o.DatabaseName = "a")
            .UseMongoDbSubscriptionStore(static o => o.DatabaseName = "b");
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.StartsWith("UseMongoDbStores and UseMongoDbSubscriptionStore give ", failure, StringComparison.Ordinal);
    }

    private Dictionary<string, string?> Section(TestDatabase database) =>
        new()
        {
            ["MsgHatch:MongoDb:ConnectionString"] = server.ConnectionString,
            ["MsgHatch:MongoDb:DatabaseName"] = database.Database.DatabaseNamespace.DatabaseName,
        };

    private static IMongoCollection<BsonDocument> Documents(TestDatabase database) => database.Collection(MongoDbSubscriptionStore.CollectionName);

    private static IEnumerable<Microsoft.Extensions.Logging.Testing.FakeLogRecord> MsgHatchLogRecords(ServiceProvider provider) =>
        provider.LogRecords().Where(static record => record.Category?.StartsWith("MsgHatch.", StringComparison.Ordinal) == true);
}
