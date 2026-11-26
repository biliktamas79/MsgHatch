using MongoDB.Bson;
using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.MongoDb.Tests.Integration;

public sealed class MongoDbRegistrySettingsStoreIntegTests(MongoDbFixture server)
    : RegistrySettingsStoreTestsBase<MongoDbRegistrySettingsStore>(
        new MongoDbRegistrySettingsStore(server.Database("registry").Database),
        _ => new MongoDbRegistrySettingsStoreTestData(server.Database("registry")))
{
    private const string _messageTypes = MongoDbRegistrySettingsStore.MessageTypeEntryCollectionName;

    private const string _channels = MongoDbRegistrySettingsStore.ChannelEntryCollectionName;

    public static TheoryData<string, string, string> InvalidDocumentRows => new()
    {
        { "disabled channels that are not an array", _messageTypes, """{ "_id": "A", "disabledChannels": "Email" }""" },
        { "invalid channel name", _messageTypes, """{ "_id": "A", "disabledChannels": ["not a name!"] }""" },
        { "invalid message type name", _messageTypes, """{ "_id": "not a name!" }""" },
        { "invalid channel entry name", _channels, """{ "_id": "not a name!" }""" },
        { "enabled that is not a boolean", _channels, """{ "_id": "Email", "enabled": "yes" }""" },
    };

    private TestDatabase Database => server.Database("registry");

    [Fact]
    public async Task SaveChannelEntryAsync_NewEntry_WritesTheFieldsWithTheNameAsTheId()
    {
        // Arrange
        const string displayName = "Shop email";
        var entry = AChannelEntry(Email, displayName: displayName, description: null, enabled: false);

        // Act
        await Store.SaveChannelEntryAsync(entry, Ct);

        // Assert
        Assert.Equal(
            new BsonDocument
            {
                { "_id", Email.Name }, { "displayName", displayName }, { "description", BsonNull.Value }, { "enabled", false }, { "revision", entry.Revision },
                { "lastModifiedAtUtc", new BsonDocument { { "d", new BsonDateTime(Now) }, { "t", Now.Ticks } } },
            },
            Assert.Single(await Database.DocumentsAsync(_channels, Ct)));
    }

    [Fact]
    public async Task SaveMessageTypeEntryAsync_NewEntry_WritesTheDisabledChannelsAsAnArrayOfNames()
    {
        // Arrange
        var entry = AMessageTypeEntry(TypeA, disabledChannels: [SignalR, Email]);

        // Act
        await Store.SaveMessageTypeEntryAsync(entry, Ct);

        // Assert
        var document = Assert.Single(await Database.DocumentsAsync(_messageTypes, Ct));
        Assert.Equal(["A", true, new BsonArray { "SignalR", "Email" }], [document["_id"], document["enabled"], document["disabledChannels"]]);
    }

    [Theory]
    [MemberData(nameof(InvalidDocumentRows))]
    public async Task GetEntriesAsync_DocumentThatAPersonChangedIntoAnInvalidValue_GivesSerializationFailed(string row, string collection, string fields)
    {
        // Arrange
        var document = BsonDocument.Parse(
            """{ "displayName": null, "description": null, "enabled": true, "disabledChannels": [], "revision": 1, "lastModifiedAtUtc": { "d": { "$date": 0 }, "t": { "$numberLong": "0" } } }""");
        document.Merge(BsonDocument.Parse(fields), overwriteExistingElements: true);
        await Database.Collection(collection).InsertOneAsync(document, cancellationToken: Ct);

        // Act
        var entries = await Store.GetEntriesAsync(Ct);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, entries.Error?.Type));
    }

    [Fact]
    public async Task SaveChannelEntryAsync_ServerThatCannotBeReached_GivesUnavailable()
    {
        // Arrange
        var store = new MongoDbRegistrySettingsStore(UnreachableServer.Database);

        // Act
        var error = await store.SaveChannelEntryAsync(AChannelEntry(), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }
}
