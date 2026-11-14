using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;

/// <summary>The test data of the MongoDB inbox store: it reads the documents in the order of their add, and a reset deletes them.</summary>
public sealed class MongoDbInboxStoreTestData(TestDatabase database) : IInboxStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);

    public async ValueTask<IReadOnlyList<ReceivedMessage>> GetStoredAsync(CancellationToken ct) =>
        [.. (await database.DocumentsAsync(MongoDbInboxStore.CollectionName, ct)).Select(StoredDocuments.Received)];
}

/// <summary>
/// The test data of the MongoDB outbox store: it reads the documents in the order of their add, and the array
/// <c>tries</c> of one document. A reset deletes the documents.
/// </summary>
public sealed class MongoDbOutboxStoreTestData(TestDatabase database) : IOutboxStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);

    public async ValueTask<IReadOnlyList<OutgoingMessage>> GetStoredAsync(CancellationToken ct) =>
        [.. (await database.DocumentsAsync(MongoDbOutboxStore.CollectionName, ct)).Select(StoredDocuments.Outgoing)];

    public async ValueTask<IReadOnlyList<PublishTry>> GetTriesAsync(Guid outgoingMessageId, CancellationToken ct)
    {
        var document = await database.Collection(MongoDbOutboxStore.CollectionName)
            .Find(new BsonDocument("delivery.id", outgoingMessageId.ToString("D")))
            .SingleAsync(ct);
        return [.. document["tries"].AsBsonArray.Select(publishTry => StoredDocuments.TryOf(outgoingMessageId, publishTry.AsBsonDocument))];
    }
}

/// <summary>The test data of the MongoDB registry settings store. A reset deletes the documents.</summary>
public sealed class MongoDbRegistrySettingsStoreTestData(TestDatabase database) : IRegistrySettingsStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);
}
