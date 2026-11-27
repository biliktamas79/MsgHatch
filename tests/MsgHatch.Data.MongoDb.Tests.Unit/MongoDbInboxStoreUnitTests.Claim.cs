using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;
using NSubstitute;

namespace MsgHatch.Data.MongoDb.Tests.Unit;

/// <summary>
/// The claim of due documents (D103, D121, D144): a document that the code cannot read stays out of the result, a permanent
/// error fails it, and a transient error keeps it leased (section 4.13). The next due time comes from the store only when
/// the claim got fewer documents than its maximum.
/// </summary>
public sealed partial class MongoDbInboxStoreUnitTests
{
    private static readonly TimeSpan _lease = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task ClaimDueAsync_DocumentWithAnEventThatIsNoObject_FailsTheDocumentAndClaimsTheNextOne()
    {
        // Arrange
        var store = Store();
        var (unreadable, readable) = (await StoredDocumentAsync(store), await StoredDocumentAsync(store));
        unreadable["event"] = "text"; // not an object
        ClaimGives(unreadable, readable);

        // Act
        var result = await store.ClaimDueAsync(maxCount: 2, TestMessages.CreatedAtUtc, _lease, Ct);

        // Assert
        Assert.Equal([readable["delivery"]["id"].AsString], result.Value.Messages.Select(static message => message.Delivery.Id.ToString("D")));
        var update = Assert.Single(_database.CallsOf(_inbox, nameof(IMongoCollection<>.UpdateOneAsync)));
        Assert.Equal(
            (new BsonDocument("_id", unreadable["_id"]).ToJson(), "Failed"),
            (DoubledDatabase.Rendered((FilterDefinition<BsonDocument>)update[0]!).ToJson(), DoubledDatabase.Rendered((UpdateDefinition<BsonDocument>)update[1]!)["$set"]["delivery.status"].AsString));
    }

    [Fact]
    public async Task ClaimDueAsync_DocumentOfANewerVersionThanTheCode_KeepsTheDocumentLeasedAndClaimsTheNextOne()
    {
        // Arrange
        var store = Store();
        var (newer, readable) = (await StoredDocumentAsync(store), await StoredDocumentAsync(store));
        newer["contentVersion"] = 3; // newer than the registered version 2
        ClaimGives(newer, readable);

        // Act
        var result = await store.ClaimDueAsync(maxCount: 2, TestMessages.CreatedAtUtc, _lease, Ct);

        // Assert
        Assert.Equal([readable["delivery"]["id"].AsString], result.Value.Messages.Select(static message => message.Delivery.Id.ToString("D")));
        Assert.Empty(_database.CallsOf(_inbox, nameof(IMongoCollection<>.UpdateOneAsync)));
    }

    [Fact]
    public async Task ClaimDueAsync_ClaimOfTheMaxCount_GivesNowAsTheNextDueTime()
    {
        // Arrange
        var store = Store();
        ClaimGives(await StoredDocumentAsync(store));

        // Act
        var result = await store.ClaimDueAsync(maxCount: 1, TestMessages.CreatedAtUtc, _lease, Ct);

        // Assert
        Assert.Equal((1, (DateTime?)TestMessages.CreatedAtUtc), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
        Assert.Empty(_database.FindsOf(_inbox));
    }

    [Fact]
    public async Task ClaimDueAsync_ClaimOfFewerDocumentsThanTheMaxCount_GivesTheEarliestDueTimeOfThePendingDocuments()
    {
        // Arrange
        _database.Finds(_inbox, BsonDocument.Parse("{ delivery: { nextTryAtUtc: { d: ISODate('2030-01-02T00:00:00Z'), t: NumberLong('640291392000000000') } } }"));

        // Act
        var result = await Store().ClaimDueAsync(maxCount: 1, TestMessages.CreatedAtUtc, _lease, Ct);

        // Assert
        Assert.Equal((0, (DateTime?)_to), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task ClaimDueAsync_NoPendingDocument_GivesNoNextDueTime()
    {
        // Act
        var result = await Store().ClaimDueAsync(maxCount: 1, TestMessages.CreatedAtUtc, _lease, Ct);

        // Assert
        Assert.Equal((0, (DateTime?)null), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    /// <summary>Adds a message through the store, and gives its inserted document.</summary>
    private async Task<BsonDocument> StoredDocumentAsync(MongoDbInboxStore store)
    {
        await store.For(_t1).AddAsync(AMessage(), Ct);
        return Assert.Single(_database.InsertsOf(_inbox)[^1]);
    }

    /// <summary>Makes the claims of the store give the documents one by one, and then no document.</summary>
    private void ClaimGives(params BsonDocument[] documents) =>
        _database.Collection(_inbox)
            .FindOneAndUpdateAsync(
                Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<UpdateDefinition<BsonDocument>>(),
                Arg.Any<FindOneAndUpdateOptions<BsonDocument, BsonDocument>>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<BsonDocument>(documents[0]),
                [.. documents.Skip(1).Select(static document => Task.FromResult<BsonDocument>(document)), Task.FromResult<BsonDocument>(null!)]);
}
