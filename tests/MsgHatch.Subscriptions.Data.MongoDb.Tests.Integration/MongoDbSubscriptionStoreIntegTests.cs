using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Channels.Email;
using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration;

public sealed class MongoDbSubscriptionStoreIntegTests(MongoDbFixture server, MongoDbAcmeAndGlobexScenario scenario)
    : SubscriptionStoreTestsBase<MongoDbSubscriptionStore>(
        MongoDbSubscriptionStoreTestData.Of(server, "store").StoreOf,
        _ => MongoDbSubscriptionStoreTestData.Of(server, "store"),
        scenario),
      IClassFixture<MongoDbAcmeAndGlobexScenario>
{
    public static TheoryData<string, BsonDocument, int> BrokenDocumentRows => new()
    {
        { "unknown level", Set("level", "Team"), 415 },
        { "owner that does not match its level", Set("level", "User"), 415 },
        { "invalid message type name", Set("type", "not a name!"), 415 },
        { "settings that are not an object", Set("settings", "{"), 415 },
        { "newer settings version", Set("settingsVersion", 2), 426 },
        { "named users that are not an array", Set("namedUsers", "2"), 415 },
        { "named users that are null", Set("namedUsers", BsonNull.Value), 415 },
        { "creation time that is a plain date", Set("createdAtUtc", new BsonDateTime(0)), 415 },
    };

    private IMongoCollection<BsonDocument> Documents => MongoDbSubscriptionStoreTestData.Of(server, "store").Documents;

    [Fact]
    public async Task AddAsync_SettingsOfAnUnregisteredChannel_GivesSerializationFailedAndStoresNothing()
    {
        // Arrange
        var subscription = Scenario.S6 with { Channel = TestSubscriptions.TestChannel, Settings = new TestChannelSettings() };

        // Act
        var error = await Store.AddAsync(subscription, Ct);

        // Assert
        Assert.Equal(((int?)415, (Subscription?)null), (CodeOf(error), (await Store.GetAsync(subscription.Id, Ct)).Value));
    }

    [Fact]
    public async Task AddAsync_SecondDefaultSubscriptionForTheSameTypeChannelAndRecipientKind_GivesConflict()
    {
        // Arrange
        await Store.AddAsync(Scenario.S1, Ct);

        // Act
        var error = await Store.AddAsync(Scenario.S1 with { Id = Scenario.LabelId(50), Settings = new EmailChannelSettings("hu") }, Ct);

        // Assert
        Assert.Equal(409, CodeOf(error));
    }

    [Fact]
    public async Task AddAsync_SecondOptOutForAllChannelsOfTheSameOwnerAndType_GivesConflict()
    {
        // Arrange
        await Store.AddAsync(Scenario.S4, Ct);

        // Act
        var error = await Store.AddAsync(Scenario.S4 with { Id = Scenario.LabelId(51) }, Ct);

        // Assert
        Assert.Equal(409, CodeOf(error));
    }

    [Fact]
    public async Task AddAsync_SubscriptionForNamedUsers_WritesTheFieldsWithNullForNoTenantAndNoUserAndTheSettingsAsADocument()
    {
        // Arrange
        var settings = BsonDocument.Parse(ScenarioContentTypes.SettingsJson.Serialize(EmailChannel.Id, Scenario.S11.Settings!).Value.Json);
        var time = new BsonDocument { { "d", new BsonDateTime(Scenario.S11.CreatedAtUtc) }, { "t", Scenario.S11.CreatedAtUtc.Ticks } };

        // Act
        await Store.AddAsync(Scenario.S11, Ct);

        // Assert
        Assert.Equal(
            new BsonDocument
            {
                { "_id", Scenario.S11.Id.ToString("D") }, { "revision", 1 }, { "tenant", BsonNull.Value }, { "level", "Company" }, { "company", "100" },
                { "user", BsonNull.Value }, { "type", "D" }, { "kind", "Channel" }, { "channel", "Email" }, { "settingsType", "Settings:Email" },
                { "settingsVersion", 1 }, { "settings", settings }, { "recipients", "NamedUsers" }, { "namedUsers", new BsonArray { "2" } },
                { "mandatory", false }, { "createdAtUtc", time }, { "lastModifiedAtUtc", time },
            },
            Assert.Single(await Documents.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(Ct)));
    }

    [Theory]
    [MemberData(nameof(BrokenDocumentRows))]
    public async Task GetAsync_DocumentThatAPersonBroke_GivesTheErrorOfTheDocument(string row, BsonDocument change, int expectedCode)
    {
        // Arrange
        await Store.AddAsync(Scenario.S11, Ct);
        await Documents.UpdateOneAsync(FilterDefinition<BsonDocument>.Empty, change, cancellationToken: Ct);

        // Act
        var stored = await Store.GetAsync(Scenario.S11.Id, Ct);

        // Assert
        Assert.Equal((row, (int?)expectedCode), (row, stored.Error?.Type.Code));
    }

    [Fact]
    public async Task FindForDeliveryAsync_DocumentWithANewerSettingsVersion_GivesUnsupportedVersion()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        await Documents.UpdateOneAsync(FilterDefinition<BsonDocument>.Empty, Set("settingsVersion", 2), cancellationToken: Ct);

        // Act
        var found = await Store.FindForDeliveryAsync(Scenario.S6.Type, [], [new UserId("1")], Ct);

        // Assert
        Assert.Equal(426, found.Error?.Type.Code);
    }

    [Fact]
    public async Task AddAsync_ServerThatCannotBeReached_GivesUnavailable()
    {
        // Arrange
        var store = new MongoDbSubscriptionStore(UnreachableServer.Database, TenantKey.None, ScenarioContentTypes.SettingsJson);

        // Act
        var error = await store.AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(503, CodeOf(error));
    }

    [Fact]
    public async Task AddAsync_FirstAdd_CreatesTheUniqueOwnerIndex()
    {
        // Act
        await Store.AddAsync(Scenario.S6, Ct);

        // Assert
        var index = Assert.Single(
            await (await Documents.Indexes.ListAsync(Ct)).ToListAsync(Ct), static index => index["name"] == "UX_MsgHatchSubscription_Owner");
        Assert.Equal(
            (new BsonDocument
            {
                { "tenant", 1 }, { "type", 1 }, { "level", 1 }, { "company", 1 }, { "user", 1 }, { "channel", 1 }, { "recipients", 1 },
            }, (BsonValue)true),
            (index["key"].AsBsonDocument, index["unique"]));
    }

    private static BsonDocument Set(string field, BsonValue value) => new("$set", new BsonDocument(field, value));
}
