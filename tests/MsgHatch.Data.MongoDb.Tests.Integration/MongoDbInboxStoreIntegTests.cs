using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.MongoDb.Tests.Integration;

public sealed class MongoDbInboxStoreIntegTests(MongoDbFixture server)
    : InboxStoreTestsBase<MongoDbInboxStore>(
        new MongoDbInboxStore(server.Database("inbox").Database, new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new MongoDbInboxStoreTestData(server.Database("inbox")))
{
    private TestDatabase Database => server.Database("inbox");

    [Fact]
    public async Task AddAsync_SameIdWithNoTenantAndATenant_WritesTwoDocumentsWithTheTenantAndTheEnumValuesAsText()
    {
        // Arrange
        var ofNoTenant = await AddAsync(AMessage(TenantKey.None));
        var ofT1 = AMessage(T1, id: ofNoTenant.Delivery.Id, flags: MessageFlags.Test);
        var id = ofNoTenant.Delivery.Id.ToString("D");

        // Act
        await TenantStore(T1).AddAsync(ofT1, Ct);

        // Assert
        Assert.Equal(
            [[BsonNull.Value, id, "Pending", "None"], ["t1", id, "Pending", "Test"]],
            (await DocumentsAsync()).Select(static document =>
                new[] { document["tenant"], document["delivery"]["id"], document["delivery"]["status"], document["flags"] }));
    }

    [Fact]
    public async Task AddAsync_TimeWithTicksBelowAMillisecond_WritesTheDateCutToTheMillisecondAndTheExactTicks()
    {
        // Arrange
        var time = Now.AddTicks(1_234_567);
        var message = AMessage(T1, nextTryAtUtc: time);

        // Act
        await TenantStore(T1).AddAsync(message, Ct);

        // Assert
        var delivery = Assert.Single(await DocumentsAsync())["delivery"];
        Assert.Equal(
            [new BsonDocument { { "d", new BsonDateTime(Now.AddMilliseconds(123)) }, { "t", time.Ticks } }, BsonNull.Value],
            [delivery["nextTryAtUtc"], delivery["lastTryAtUtc"]]);
    }

    [Fact]
    public async Task GetAsync_TimesWithTicksBelowAMillisecond_GivesTheExactTimesBack()
    {
        // Arrange
        var message = AMessage(T1) with
        {
            Delivery = TestMessages.ADeliveryState() with
            {
                CreatedAtUtc = Now.AddTicks(1), NextTryAtUtc = Now.AddTicks(2), ExpiresAtUtc = Now.AddTicks(3),
            },
        };
        await AddAsync(message);

        // Act
        var stored = await TenantStore(T1).GetAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal(message.Delivery, stored.Value!.Delivery);
    }

    [Fact]
    public async Task ClaimDueAsync_TwoMessagesDueInOneMillisecond_ClaimsTheMessageThatIsDueFirst()
    {
        // Arrange
        await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddTicks(2)));
        var dueFirst = await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddTicks(1)));
        Time.Advance(TimeSpan.FromMilliseconds(1));

        // Act
        var result = await ClaimAsync(maxCount: 1);

        // Assert
        Assert.Equal(dueFirst.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task ClaimDueAsync_CancelledToken_GivesOperationCancelled()
    {
        // Arrange
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await cancelled.CancelAsync();

        // Act
        var result = await Store.ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), cancelled.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
    }

    [Fact]
    public async Task AddAsync_MessageOfAnUnregisteredType_ReturnsSerializationFailedAndAddsNothing()
    {
        // Arrange
        var message = AMessage(T1) with { Type = new MessageType("Unknown") };

        // Act
        var error = await TenantStore(T1).AddAsync(message, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, error?.Type);
        Assert.Empty(await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task AddAsync_ServerThatCannotBeReached_ReturnsUnavailable()
    {
        // Arrange
        var store = new MongoDbInboxStore(UnreachableServer.Database, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var error = await store.For(T1).AddAsync(AMessage(T1), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_DocumentWithAnEventThatIsNotAnObject_FailsTheDocumentAndClaimsTheOtherMessage()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        var message = await AddAsync(AMessage(T1));
        await ChangeAsync(unreadable, new BsonDocument("$set", new BsonDocument("event", "{ not json")));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(["Failed", "SerializationFailed", BsonNull.Value], await StateOfAsync(unreadable));
    }

    [Fact]
    public async Task ClaimDueAsync_DocumentWithAValueThatJsonDoesNotHave_FailsTheDocumentAndClaimsTheOtherMessage()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        var message = await AddAsync(AMessage(T1));
        await ChangeAsync(unreadable, new BsonDocument("$set", new BsonDocument("event.text", ObjectId.GenerateNewId(0))));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(["Failed", "SerializationFailed", BsonNull.Value], await StateOfAsync(unreadable));
    }

    [Fact]
    public async Task QueryAsync_CompletedMessage_GivesEachTimeWithTheKindUtc()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        await Store.CompleteAsync(message, Now, Ct);

        // Act
        var page = await TenantStore(T1).QueryAsync(new InboxQuery(), Ct);

        // Assert
        var delivery = Assert.Single(page.Value.Items).Delivery;
        Assert.Equal([DateTimeKind.Utc, DateTimeKind.Utc, DateTimeKind.Utc], [delivery.CreatedAtUtc.Kind, delivery.NextTryAtUtc.Kind, delivery.LastTryAtUtc!.Value.Kind]);
    }

    [Fact]
    public async Task GetAsync_EventWithNumbers_GivesEachNumberBackWithItsDigitsAndKeepsTheIntegersAsIntegers()
    {
        // Arrange
        var numbers = new NumbersEvent(int.MaxValue, long.MinValue, 19.90m, 0.1, 5E-324, double.MaxValue, Raw: null);
        var message = ANumbersMessage(numbers);
        var store = NumbersStore();
        await store.For(T1).AddAsync(message, Ct);

        // Act
        var stored = (ReceivedMessage<NumbersEvent>)(await store.For(T1).GetAsync(message.Delivery.Id, Ct)).Value!;

        // Assert
        Assert.Equal((numbers, 2), (stored.Event, stored.Event.Money.Scale));
        Assert.Equal(
            [BsonType.Int32, BsonType.Int64, BsonType.Decimal128, BsonType.Decimal128, BsonType.Decimal128, BsonType.Decimal128, BsonType.Null],
            Assert.Single(await DocumentsAsync())["event"].AsBsonDocument.Select(static field => field.Value.BsonType));
    }

    [Fact]
    public async Task AddAsync_EventWithANumberOfMoreThan34Digits_ReturnsSerializationFailedAndAddsNothing()
    {
        // Arrange
        using var raw = JsonDocument.Parse("12345678901234567890123456789012345");
        var message = ANumbersMessage(new NumbersEvent(0, 0, 0m, 0, 0, 0, raw.RootElement));

        // Act
        var error = await NumbersStore().For(T1).AddAsync(message, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, error?.Type);
        Assert.Empty(await DocumentsAsync());
    }

    [Fact]
    public async Task ClaimDueAsync_FirstClaim_CreatesTheUniqueKeyIndexAndThePartialClaimIndex()
    {
        // Act
        await ClaimAsync();

        // Assert
        var indexes = await (await Database.Collection(MongoDbInboxStore.CollectionName).Indexes.ListAsync(Ct)).ToListAsync(Ct);
        Assert.Equal(
            [
                ("UX_MsgHatchInbox_Key", new BsonDocument { { "tenant", 1 }, { "delivery.id", 1 } }, BsonBoolean.True, BsonNull.Value),
                ("IX_MsgHatchInbox_Due", new BsonDocument { { "delivery.nextTryAtUtc.t", 1 }, { "_id", 1 } }, BsonNull.Value,
                    new BsonDocument("delivery.status", "Pending")),
            ],
            indexes.Where(static index => index["name"].AsString is "UX_MsgHatchInbox_Key" or "IX_MsgHatchInbox_Due")
                .Select(static index => (index["name"].AsString, index["key"].AsBsonDocument, index.GetValue("unique", BsonNull.Value),
                    index.GetValue("partialFilterExpression", BsonNull.Value))));
    }

    private MongoDbInboxStore NumbersStore() =>
        new(Database.Database, new MsgHatchJson(TestContentTypes.ARegistryOf(TestContentTypes.TestEventV2, NumbersContentTypes.NumbersEventV1)));

    private ReceivedMessage<NumbersEvent> ANumbersMessage(NumbersEvent numbers) =>
        TestMessages.AReceivedMessage(numbers, T1.Tenant, contentVersion: 1, delivery: TestMessages.ADeliveryState() with { CreatedAtUtc = Now })
            with { Type = new MessageType("NumbersEvent") };

    private Task<List<BsonDocument>> DocumentsAsync() => Database.DocumentsAsync(MongoDbInboxStore.CollectionName, Ct);

    private Task ChangeAsync(ReceivedMessage message, BsonDocument update) =>
        Database.ChangeAsync(MongoDbInboxStore.CollectionName, message.Delivery.Id, update, Ct);

    /// <summary>The status, the error name of the last error, and the lease end of the document of a message.</summary>
    private async Task<BsonValue[]> StateOfAsync(ReceivedMessage message)
    {
        var delivery = (await DocumentsAsync()).Single(document => document["delivery"]["id"] == message.Delivery.Id.ToString("D"))["delivery"];
        return [delivery["status"], delivery["lastError"].AsString.Split(':')[0], delivery["leaseUntilUtc"]];
    }
}
