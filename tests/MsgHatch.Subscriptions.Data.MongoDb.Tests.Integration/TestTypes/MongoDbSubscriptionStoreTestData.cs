using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

[assembly: AssemblyFixture(typeof(MongoDbFixture))]

namespace MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration.TestTypes;

/// <summary>
/// The test data of the MongoDB subscription store: one database of the MongoDB of the fixture. A reset deletes the
/// documents of the collection of all tenants, and keeps the collection with its indexes.
/// </summary>
public sealed class MongoDbSubscriptionStoreTestData(TestDatabase database) : ISubscriptionStoreTestData
{
    public TestDatabase Database { get; } = database;

    /// <summary>The documents of the store, in no order.</summary>
    public IMongoCollection<BsonDocument> Documents => Database.Collection(MongoDbSubscriptionStore.CollectionName);

    /// <summary>The test data of the database of one name. The fixture gives the same database for each call with that name.</summary>
    public static MongoDbSubscriptionStoreTestData Of(MongoDbFixture server, string name) => new(server.Database(name));

    /// <summary>Gives the store of a tenant key on the database.</summary>
    public MongoDbSubscriptionStore StoreOf(TenantKey key) => new(Database.Database, key, ScenarioContentTypes.SettingsJson);

    public async ValueTask ResetAsync(CancellationToken ct) => await Documents.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);
}
