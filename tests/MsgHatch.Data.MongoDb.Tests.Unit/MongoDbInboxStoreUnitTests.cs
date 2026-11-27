using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;
using NSubstitute;

namespace MsgHatch.Data.MongoDb.Tests.Unit;

/// <summary>
/// The decisions of <see cref="MongoDbInboxStore"/> that need no server (D330), on a database of test doubles: the
/// conversion of the JSON document into BSON and back (D184, D185), the errors of the driver, and the index creation. The
/// behavior of a server is in the integration tests.
/// </summary>
public sealed partial class MongoDbInboxStoreUnitTests
{
    private const string _inbox = "MsgHatchInbox";

    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private readonly DoubledDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string> JsonToBsonRows => new()
    {
        { "the largest 32-bit integer", "2147483647", "{ v: 2147483647 }" },
        { "a 64-bit integer above the 32-bit range", "2147483648", "{ v: NumberLong('2147483648') }" },
        { "the smallest 64-bit integer", "-9223372036854775808", "{ v: NumberLong('-9223372036854775808') }" },
        { "an integer above the 64-bit range", "9223372036854775808", "{ v: NumberDecimal('9223372036854775808') }" },
        { "a fraction with a trailing zero", "19.90", "{ v: NumberDecimal('19.90') }" },
        { "a number with 34 digits", "1234567890123456789012345678901234", "{ v: NumberDecimal('1234567890123456789012345678901234') }" },
        { "a number with an exponent", "1E+400", "{ v: NumberDecimal('1E+400') }" },
        { "true", "true", "{ v: true }" },
        { "false", "false", "{ v: false }" },
        { "null", "null", "{ v: null }" },
        { "a text with an accented letter", "\"é\"", "{ v: 'é' }" },
        { "an array of each kind", "[1,\"a\",[]]", "{ v: [1, 'a', []] }" },
        { "an object in an object", "{\"a\":{\"b\":null}}", "{ v: { a: { b: null } } }" },
    };

    public static TheoryData<string, string, string> BsonToJsonRows => new()
    {
        { "a double", "{ v: 0.5 }", "0.5" },
        { "a 32-bit integer", "{ v: 7 }", "7" },
        { "a 64-bit integer", "{ v: NumberLong('9007199254740993') }", "9007199254740993" },
        { "a decimal with a trailing zero", "{ v: NumberDecimal('19.90') }", "19.90" },
        { "true", "{ v: true }", "true" },
        { "false", "{ v: false }", "false" },
        { "a text", "{ v: 'text' }", "\"text\"" },
        { "an array", "{ v: [1, 'a'] }", "[1,\"a\"]" },
        { "a document", "{ v: { a: 1 } }", "{\"a\":1}" },
        { "a time with a tick below a millisecond", "{ v: { d: ISODate('2030-01-01T00:00:00Z'), t: NumberLong('640290528000000001') } }", "\"2030-01-01T00:00:00.0000001Z\"" },
        { "a document with d as a text", "{ v: { d: 'x', t: NumberLong('1') } }", "{\"d\":\"x\",\"t\":1}" },
        { "a document with t and no d", "{ v: { t: NumberLong('1'), x: 1 } }", "{\"t\":1,\"x\":1}" },
    };

    public static TheoryData<string, string> BsonWithNoJsonRows => new()
    {
        { "an ObjectId", "{ v: ObjectId('0123456789abcdef01234567') }" },
        { "a date outside a time document", "{ v: ISODate('2030-01-01T00:00:00Z') }" },
        { "a time document with t as a 32-bit integer", "{ v: { d: ISODate('2030-01-01T00:00:00Z'), t: 1 } }" },
    };

    public static TheoryData<string, Exception, int> DriverExceptionRows => new()
    {
        { "MongoException", new MongoException("The server is not reachable."), 503 },
        { "TimeoutException", new TimeoutException(), 503 },
        { "FormatException", new FormatException(), 415 },
        { "ArgumentException", new ArgumentException("The value is not valid."), 415 },
        { "JsonException", new JsonException(), 415 },
        { "InvalidCastException", new InvalidCastException(), 415 },
        { "KeyNotFoundException", new KeyNotFoundException(), 415 },
        { "BsonException", new BsonException("The value is not valid."), 415 },
    };

    [Theory]
    [MemberData(nameof(JsonToBsonRows))]
    public async Task AddAsync_JsonValueOfTheEvent_InsertsItsBsonValue(string row, string json, string expected)
    {
        // Arrange
        using var raw = JsonDocument.Parse(json);
        var message = ANumbersMessage(raw.RootElement);

        // Act
        var error = await NumbersStore().For(_t1).AddAsync(message, Ct);

        // Assert
        var expectedValue = BsonDocument.Parse(expected)["v"];
        var inserted = Assert.Single(Assert.Single(_database.InsertsOf(_inbox)))["event"]["raw"];
        Assert.Equal((row, (Error?)null, expectedValue.BsonType, expectedValue), (row, error, inserted.BsonType, inserted));
    }

    [Fact]
    public async Task AddAsync_NumberOfMoreThan34Digits_ReturnsSerializationFailedAndInsertsNothing()
    {
        // Arrange
        using var raw = JsonDocument.Parse("12345678901234567890123456789012345");
        var message = ANumbersMessage(raw.RootElement);

        // Act
        var error = await NumbersStore().For(_t1).AddAsync(message, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, error?.Type);
        Assert.Empty(_database.InsertsOf(_inbox));
    }

    [Theory]
    [MemberData(nameof(BsonToJsonRows))]
    public async Task GetAsync_BsonValueOfTheStoredEvent_GivesItsJsonText(string row, string stored, string expected)
    {
        // Arrange
        var store = NumbersStore();
        var message = await StoredNumbersMessageAsync(store, BsonDocument.Parse(stored)["v"]);

        // Act
        var result = await store.For(_t1).GetAsync(message.Delivery.Id, Ct);

        // Assert
        var read = Assert.IsType<ReceivedMessage<NumbersEvent>>(result.Value);
        Assert.Equal((row, expected), (row, read.Event.Raw?.GetRawText()));
    }

    [Theory]
    [MemberData(nameof(BsonWithNoJsonRows))]
    public async Task GetAsync_StoredValueThatJsonDoesNotHave_ReturnsSerializationFailed(string row, string stored)
    {
        // Arrange
        var store = NumbersStore();
        var message = await StoredNumbersMessageAsync(store, BsonDocument.Parse(stored)["v"]);

        // Act
        var result = await store.For(_t1).GetAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal((row, (ErrorType?)Errors.SerializationFailed), (row, result.Error?.Type));
    }

    [Theory]
    [MemberData(nameof(DriverExceptionRows))]
    public async Task AddAsync_ExceptionOfTheInsert_ReturnsTheErrorCodeOfTheRowWithTheException(string row, Exception exception, int expectedCode)
    {
        // Arrange
        InsertThrows(exception);

        // Act
        var error = await Store().For(_t1).AddAsync(AMessage(), Ct);

        // Assert
        Assert.Equal((row, expectedCode, exception), (row, error?.Type.Code, error?.Exception));
    }

    [Fact]
    public async Task AddAsync_WriteErrorThatIsNoDuplicateKey_ReturnsUnavailable()
    {
        // Arrange
        var connection = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));
        InsertThrows(new MongoWriteException(connection, writeError: null, writeConcernError: null, innerException: null));

        // Act
        var error = await Store().For(_t1).AddAsync(AMessage(), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    [Fact]
    public async Task AddAsync_ExceptionThatIsNoErrorOfTheStore_Throws()
    {
        // Arrange
        InsertThrows(new InvalidOperationException());

        // Act
        var exception = await Record.ExceptionAsync(async () => await Store().For(_t1).AddAsync(AMessage(), Ct));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task AddAsync_CancelledToken_ReturnsOperationCancelled()
    {
        // Arrange
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await cancel.CancelAsync();
        InsertThrows(new OperationCanceledException(cancel.Token));

        // Act
        var error = await Store().For(_t1).AddAsync(AMessage(), cancel.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, error?.Type);
    }

    [Fact]
    public async Task AddAsync_MessageOfAnotherTenant_ReturnsValidationFailedAndInsertsNothing()
    {
        // Arrange
        var message = AMessage() with { Tenant = new TenantId("t2") };

        // Act
        var error = await Store().For(_t1).AddAsync(message, Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, error?.Type);
        Assert.Empty(_database.InsertsOf(_inbox));
    }

    [Fact]
    public async Task ClaimDueAsync_TwoStoresOnOneDatabase_CreatesTheIndexesOneTime()
    {
        // Arrange
        await Store().ClaimDueAsync(1, TestMessages.CreatedAtUtc, TimeSpan.FromMinutes(1), Ct);

        // Act
        await Store().ClaimDueAsync(1, TestMessages.CreatedAtUtc, TimeSpan.FromMinutes(1), Ct);

        // Assert
        var creation = Assert.Single(IndexCreations());
        Assert.Equal(
            ["UX_MsgHatchInbox_Key", "IX_MsgHatchInbox_Due", "IX_MsgHatchInbox_Flags", "IX_MsgHatchInbox_Created"],
            ((IEnumerable<CreateIndexModel<BsonDocument>>)creation[0]!).Select(static index => index.Options.Name));
    }

    [Fact]
    public async Task ClaimDueAsync_IndexCreationThatFails_ReturnsUnavailable()
    {
        // Arrange
        IndexCreationThrowsOneTime();

        // Act
        var result = await Store().ClaimDueAsync(1, TestMessages.CreatedAtUtc, TimeSpan.FromMinutes(1), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_CallAfterAFailedIndexCreation_CreatesTheIndexesAgain()
    {
        // Arrange
        IndexCreationThrowsOneTime();
        var store = Store();
        await store.ClaimDueAsync(1, TestMessages.CreatedAtUtc, TimeSpan.FromMinutes(1), Ct);

        // Act
        var result = await store.ClaimDueAsync(1, TestMessages.CreatedAtUtc, TimeSpan.FromMinutes(1), Ct);

        // Assert
        Assert.Equal((false, 2), (result.IsError, IndexCreations().Count));
    }

    private MongoDbInboxStore Store() => new(_database.Database, new MsgHatchJson(TestContentTypes.ARegistry()));

    private MongoDbInboxStore NumbersStore() =>
        new(_database.Database, new MsgHatchJson(TestContentTypes.ARegistryOf(TestContentTypes.TestEventV2, NumbersContentTypes.NumbersEventV1)));

    private static ReceivedMessage<TestEvent> AMessage() => TestMessages.AReceivedMessage(new TestEvent("text"), _t1.Tenant);

    private static ReceivedMessage<NumbersEvent> ANumbersMessage(JsonElement? raw) =>
        TestMessages.AReceivedMessage(new NumbersEvent(1, 2, 3m, 0.5, 0.25, 4, raw), _t1.Tenant, contentVersion: 1)
            with { Type = new MessageType("NumbersEvent") };

    /// <summary>Adds a numbers message through the store, and makes each find give its inserted document with <paramref name="raw"/> as the raw value.</summary>
    private async Task<ReceivedMessage> StoredNumbersMessageAsync(MongoDbInboxStore store, BsonValue raw)
    {
        var message = ANumbersMessage(raw: null);
        await store.For(_t1).AddAsync(message, Ct);
        var document = Assert.Single(Assert.Single(_database.InsertsOf(_inbox)));
        document["event"]["raw"] = raw;
        _database.Finds(_inbox, document);
        return message;
    }

    private void InsertThrows(Exception exception) =>
        _database.Collection(_inbox)
            .InsertManyAsync(Arg.Any<IEnumerable<BsonDocument>>(), Arg.Any<InsertManyOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(exception));

    private void IndexCreationThrowsOneTime() =>
        _database.Collection(_inbox).Indexes
            .CreateManyAsync(Arg.Any<IEnumerable<CreateIndexModel<BsonDocument>>>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<IEnumerable<string>>(new MongoException("The server is not reachable.")),
                Task.FromResult<IEnumerable<string>>([]));

    private IReadOnlyList<object?[]> IndexCreations() =>
        [.. _database.Collection(_inbox).Indexes.ReceivedCalls()
            .Where(static call => call.GetMethodInfo().Name == nameof(IMongoIndexManager<>.CreateManyAsync))
            .Select(static call => call.GetArguments())];
}
