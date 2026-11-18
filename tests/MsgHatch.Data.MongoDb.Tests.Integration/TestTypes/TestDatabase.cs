using MongoDB.Bson;
using MongoDB.Driver;

namespace MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;

/// <summary>
/// One database of a test class on the MongoDB of the fixture. A reset deletes the documents of each collection of the
/// stores, and keeps the collections with their indexes, because a store creates its indexes one time for each database
/// in the process.
/// </summary>
public sealed class TestDatabase(IMongoDatabase database)
{
    private static readonly string[] _collections =
    [
        MongoDbInboxStore.CollectionName, MongoDbOutboxStore.CollectionName, MongoDbOutboxStore.ChannelStateCollectionName,
        MongoDbRegistrySettingsStore.ChannelEntryCollectionName, MongoDbRegistrySettingsStore.MessageTypeEntryCollectionName,
    ];

    public IMongoDatabase Database { get; } = database;

    public async ValueTask ResetAsync(CancellationToken ct)
    {
        foreach (var collection in _collections)
        {
            await Collection(collection).DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);
        }
    }

    public IMongoCollection<BsonDocument> Collection(string name) => Database.GetCollection<BsonDocument>(name);

    /// <summary>The documents of a collection in the order of their ObjectId, which is the order of their add.</summary>
    public Task<List<BsonDocument>> DocumentsAsync(string collection, CancellationToken ct) =>
        Collection(collection).Find(FilterDefinition<BsonDocument>.Empty).Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync(ct);

    /// <summary>Changes the document of one message id with a raw update, as a person can do it in the tools of MongoDB.</summary>
    public Task ChangeAsync(string collection, Guid messageId, BsonDocument update, CancellationToken ct) =>
        Collection(collection).UpdateOneAsync(new BsonDocument("delivery.id", messageId.ToString("D")), update, cancellationToken: ct);
}
