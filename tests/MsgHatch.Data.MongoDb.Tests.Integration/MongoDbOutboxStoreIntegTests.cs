using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.MongoDb.Tests.Integration;

public sealed class MongoDbOutboxStoreIntegTests(MongoDbFixture server)
    : OutboxStoreTestsBase<MongoDbOutboxStore>(
        new MongoDbOutboxStore(server.Database("outbox").Database, new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new MongoDbOutboxStoreTestData(server.Database("outbox")))
{
    private TestDatabase Database => server.Database("outbox");

    private BsonDocument StartTime => new() { { "d", new BsonDateTime(Now) }, { "t", Now.Ticks } };

    [Fact]
    public async Task AddAsync_MessageWithNoTenantAndATrace_WritesTheEnumValuesAsTextANullTenantTheTraceAndNoTries()
    {
        // Arrange
        var message = AMessage(TenantKey.None) with { Trace = new TestTrace("reason") };

        // Act
        await TenantStore(TenantKey.None).AddAsync([message], Ct);

        // Assert
        var document = Assert.Single(await DocumentsAsync());
        Assert.Equal(
            [BsonNull.Value, "Pending", StartTime, StartTime, BsonNull.Value, "User", "None",
                new BsonDocument { { "$type", "Trace:Test@1" }, { "reason", "reason" } }, new BsonArray()],
            [document["tenant"], document["delivery"]["status"], document["delivery"]["createdAtUtc"], document["delivery"]["nextTryAtUtc"],
                document["delivery"]["leaseUntilUtc"], document["target"]["kind"], document["flags"], document["trace"], document["tries"]]);
    }

    [Fact]
    public async Task GetAsync_MessageWithATrace_GivesTheTraceBack()
    {
        // Arrange
        var message = AMessage(T1) with { Trace = new TestTrace("reason") };
        await TenantStore(T1).AddAsync([message], Ct);

        // Act
        var history = await TenantStore(T1).GetAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal(new TestTrace("reason"), history.Value!.Message.Trace);
    }

    [Fact]
    public async Task AddAsync_BatchOfThreeMessages_GivesTheDocumentsObjectIdsInTheOrderOfTheBatch()
    {
        // Arrange
        OutgoingMessage[] batch = [AMessage(T1), AMessage(T1), AMessage(T1)];

        // Act
        await TenantStore(T1).AddAsync(batch, Ct);

        // Assert
        Assert.Equal(
            batch.Select(static message => message.Delivery.Id.ToString("D")),
            (await DocumentsAsync()).Select(static document => document["delivery"]["id"].AsString));
    }

    [Fact]
    public async Task AddAsync_BatchWithAStoredIdFirst_StoresTheNewMessagesAfterIt()
    {
        // Arrange
        var stored = await AddAsync(AMessage(T1));
        OutgoingMessage[] batch = [stored, AMessage(T1), AMessage(T1)];

        // Act
        var error = await TenantStore(T1).AddAsync(batch, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(
            batch.Select(static message => message.Delivery.Id.ToString("D")),
            (await DocumentsAsync()).Select(static document => document["delivery"]["id"].AsString));
    }

    [Fact]
    public async Task AddAsync_MessageOfAnUnregisteredType_ReturnsSerializationFailedAndAddsNothingOfTheBatch()
    {
        // Arrange
        OutgoingMessage[] batch = [AMessage(T1), AMessage(T1) with { ContentType = "Test:Unknown" }];

        // Act
        var error = await TenantStore(T1).AddAsync(batch, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, error?.Type);
        Assert.Empty(await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task SetChannelDownAsync_ReasonErrors_WritesTheReasonAndTheTimes()
    {
        // Act
        await Store.SetChannelDownAsync(new ChannelDown(TestChannel.Id, ChannelDownReason.Errors, Now, Now, 503, "Unavailable", Note: null), Ct);

        // Assert
        Assert.Equal(
            new BsonDocument
            {
                { "_id", "Test" }, { "reason", "Errors" }, { "sinceUtc", StartTime }, { "nextCheckAtUtc", StartTime }, { "errorCode", 503 },
                { "errorName", "Unavailable" }, { "note", BsonNull.Value },
            },
            Assert.Single(await Database.DocumentsAsync(MongoDbOutboxStore.ChannelDownCollectionName, Ct)));
    }

    [Fact]
    public async Task ClaimDueAsync_SecondStoreOnTheSameDatabase_ClaimsTheMessageOfTheFirstStore()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var second = new MongoDbOutboxStore(Database.Database, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var result = await second.ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task RecordTryAsync_TryNumberThatIsAlreadyRecorded_ChangesNothingAndKeepsOneTry()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var publishTry = ATransientErrorTry(message);
        await Store.RecordTryAsync(message, publishTry, Now.AddMinutes(1), Ct);
        var document = Assert.Single(await DocumentsAsync());

        // Act
        var error = await Store.RecordTryAsync(message, publishTry, Now.AddMinutes(1), Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal((document, 1), (Assert.Single(await DocumentsAsync()), document["tries"].AsBsonArray.Count));
    }

    [Fact]
    public async Task RecordTryAsync_FirstTry_WritesTheTryWithEachTimeAsADateAndTicks()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));

        // Act
        await Store.RecordTryAsync(message, ATransientErrorTry(message), Now.AddMinutes(1), Ct);

        // Assert
        Assert.Equal(
            new BsonDocument
            {
                { "tryNumber", 1 }, { "startedAtUtc", StartTime },
                { "finishedAtUtc", new BsonDocument { { "d", new BsonDateTime(Now.AddSeconds(1)) }, { "t", Now.AddSeconds(1).Ticks } } },
                { "outcome", "TransientError" }, { "providerMessageId", BsonNull.Value }, { "errorCode", 429 }, { "errorName", "RateLimited" },
                { "errorMessage", BsonNull.Value },
            },
            Assert.Single(Assert.Single(await DocumentsAsync())["tries"].AsBsonArray));
    }

    [Fact]
    public async Task ClaimDueAsync_ClaimOfMaxCountDocuments_GivesNowAsTheNextDueTimeBecauseMoreDocumentsCanBeDue()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddSeconds(5)));

        // Act
        var result = await ClaimAsync(maxCount: 1);

        // Assert
        Assert.Equal((1, (DateTime?)Now), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task ClaimDueAsync_TwentyMessagesWithTheSameDueTime_ClaimsThemInTheOrderOfTheirAdd()
    {
        // Arrange
        OutgoingMessage[] messages = [.. Enumerable.Range(0, 20).Select(_ => AMessage(T1))];
        await TenantStore(T1).AddAsync(messages, Ct);

        // Act
        var result = await ClaimAsync(maxCount: 20);

        // Assert
        Assert.Equal(messages.Select(static m => m.Delivery.Id), result.Value.Messages.Select(static m => m.Delivery.Id));
    }

    [Fact]
    public async Task ClaimDueAsync_SameDueTimeAndALaterCreationTimeOfTheFirstAdd_ClaimsInTheOrderOfTheObjectIdsOfTheCreationTimes()
    {
        // Arrange
        var addedFirst = AMessage(T1);
        addedFirst = addedFirst with { Delivery = addedFirst.Delivery with { CreatedAtUtc = Now.AddSeconds(1) } };
        var addedSecond = AMessage(T1);
        await TenantStore(T1).AddAsync([addedFirst], Ct);
        await TenantStore(T1).AddAsync([addedSecond], Ct);

        // Act
        var result = await ClaimAsync(maxCount: 2);

        // Assert
        Assert.Equal([addedSecond.Delivery.Id, addedFirst.Delivery.Id], result.Value.Messages.Select(static m => m.Delivery.Id));
    }

    [Fact]
    public async Task ClaimDueAsync_DocumentWithAnEnvelopeThatIsNotAnObject_FailsTheDocumentAndClaimsTheOtherMessage()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        var message = await AddAsync(AMessage(T1));
        await ChangeAsync(unreadable, new BsonDocument("$set", new BsonDocument("envelope", "{ not json")));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        var delivery = (await DocumentsAsync())[0]["delivery"];
        Assert.Equal(["Failed", "SerializationFailed", BsonNull.Value], [delivery["status"], delivery["lastError"].AsString.Split(':')[0], delivery["leaseUntilUtc"]]);
    }

    [Fact]
    public async Task ClaimDueAsync_DocumentWithANewerVersionThanTheCode_StaysPendingWithItsLeaseEndAsTheNextDueTime()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        await ChangeAsync(message, new BsonDocument("$set", new BsonDocument("contentVersion", 2)));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)Now.AddSeconds(30)), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
        var delivery = Assert.Single(await DocumentsAsync())["delivery"];
        Assert.Equal(["Pending", Now.AddSeconds(30).Ticks], [delivery["status"], delivery["nextTryAtUtc"]["t"]]);
    }

    [Fact]
    public async Task ClaimDueAsync_DownDocumentWithAnUnknownReason_ReturnsSerializationFailed()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await Store.SetChannelDownAsync(
            new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance"), Ct);
        await Database.Collection(MongoDbOutboxStore.ChannelDownCollectionName).UpdateOneAsync(
            FilterDefinition<BsonDocument>.Empty, new BsonDocument("$set", new BsonDocument("reason", "Unknown")), cancellationToken: Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(Errors.SerializationFailed, result.Error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_ServerThatCannotBeReached_ReturnsUnavailable()
    {
        // Arrange
        var store = new MongoDbOutboxStore(UnreachableServer.Database, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var result = await store.ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task QueryAsync_ClaimedMessage_GivesEachTimeWithTheKindUtc()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await ClaimAsync();

        // Act
        var page = await TenantStore(T1).QueryAsync(new OutboxQuery(), Ct);

        // Assert
        var delivery = Assert.Single(page.Value.Items).Delivery;
        Assert.Equal([DateTimeKind.Utc, DateTimeKind.Utc, DateTimeKind.Utc], [delivery.CreatedAtUtc.Kind, delivery.NextTryAtUtc.Kind, delivery.LeaseUntilUtc!.Value.Kind]);
    }

    [Fact]
    public async Task ClaimDueAsync_FirstClaim_CreatesThePartialClaimIndexWithTheIdAsItsLastKey()
    {
        // Act
        await ClaimAsync();

        // Assert
        var indexes = await (await Database.Collection(MongoDbOutboxStore.CollectionName).Indexes.ListAsync(Ct)).ToListAsync(Ct);
        var due = Assert.Single(indexes, static index => index["name"] == "IX_MsgHatchOutbox_Due");
        Assert.Equal(
            (new BsonDocument { { "channel", 1 }, { "delivery.nextTryAtUtc.t", 1 }, { "_id", 1 } }, new BsonDocument("delivery.status", "Pending")),
            (due["key"].AsBsonDocument, due["partialFilterExpression"].AsBsonDocument));
    }

    private PublishTry ATransientErrorTry(OutgoingMessage message) =>
        new(message.Delivery.Id, 1, Now, Now.AddSeconds(1), PublishOutcome.TransientError, ProviderMessageId: null, 429, "RateLimited", ErrorMessage: null);

    private Task<List<BsonDocument>> DocumentsAsync() => Database.DocumentsAsync(MongoDbOutboxStore.CollectionName, Ct);

    private Task ChangeAsync(OutgoingMessage message, BsonDocument update) =>
        Database.ChangeAsync(MongoDbOutboxStore.CollectionName, message.Delivery.Id, update, Ct);
}
