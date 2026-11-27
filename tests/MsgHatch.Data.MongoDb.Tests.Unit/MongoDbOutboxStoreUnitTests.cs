using MongoDB.Bson;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.MongoDb.Tests.Unit;

/// <summary>
/// The decisions of <see cref="MongoDbOutboxStore"/> that need no server (D330), on a database of test doubles: the
/// filters that only the outbox has, the guards of an add, the counts of each channel, and the state documents of a
/// channel (D106, D277). The filters that the inbox shares are in <see cref="MongoDbInboxStoreUnitTests"/>.
/// </summary>
public sealed class MongoDbOutboxStoreUnitTests
{
    private const string _outbox = "MsgHatchOutbox";

    private const string _states = "MsgHatchChannelState";

    private const string _downDocument =
        "{ channel: 'Email', version: 1, kind: 'Down', sinceUtc: { d: ISODate('2030-01-01T00:00:00Z'), t: NumberLong('640290528000000000') }, "
        + "previousKind: null, previousDurationMs: null, reason: 'Errors', "
        + "nextCheckAtUtc: { d: ISODate('2030-01-02T00:00:00Z'), t: NumberLong('640291392000000000') }, errorCode: 503, errorName: 'Unavailable', note: 'note' }";

    private const string _upDocument =
        "{ channel: 'Email', version: 2, kind: 'Up', sinceUtc: { d: ISODate('2030-01-02T00:00:00Z'), t: NumberLong('640291392000000000') }, "
        + "previousKind: 'Down', previousDurationMs: NumberLong('86400000'), reason: null, nextCheckAtUtc: null, errorCode: null, errorName: null, note: null }";

    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static readonly ChannelId _email = new("Email");

    private static readonly DateTime _from = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime _to = new(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private readonly DoubledDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, OutboxQuery, string> QueryFilterRows => new()
    {
        { "a channel", new OutboxQuery(Channel: new ChannelId("Email")), "{ tenant: 't1', flags: { $ne: 'Test' }, channel: 'Email' }" },
        {
            "a target", new OutboxQuery(Target: new DeliveryTarget(DeliveryTargetKind.User, "u1")),
            "{ tenant: 't1', flags: { $ne: 'Test' }, 'target.kind': 'User', 'target.id': 'u1' }"
        },
        {
            "a source message", new OutboxQuery(SourceMessageId: new Guid("11111111-1111-1111-1111-111111111111")),
            "{ tenant: 't1', flags: { $ne: 'Test' }, sourceMessageId: '11111111-1111-1111-1111-111111111111' }"
        },
        { "a message type", new OutboxQuery(Type: new MessageType("TestEvent")), "{ tenant: 't1', flags: { $ne: 'Test' }, type: 'TestEvent' }" },
    };

    [Theory]
    [MemberData(nameof(QueryFilterRows))]
    public async Task QueryAsync_FilterOfTheOutbox_FindsWithTheFilterOfTheRow(string row, OutboxQuery query, string expected)
    {
        // Act
        await Store().For(_t1).QueryAsync(query, Ct);

        // Assert
        var find = Assert.Single(_database.FindsOf(_outbox));
        Assert.Equal((row, BsonDocument.Parse(expected).ToJson()), (row, DoubledDatabase.Rendered(find.Filter).ToJson()));
    }

    [Fact]
    public async Task AddAsync_EmptyBatch_ReturnsNoErrorAndInsertsNothing()
    {
        // Act
        var error = await Store().For(_t1).AddAsync([], Ct);

        // Assert
        Assert.Null(error);
        Assert.Empty(_database.InsertsOf(_outbox));
    }

    [Fact]
    public async Task AddAsync_BatchWithAMessageOfAnotherTenant_ReturnsValidationFailedAndInsertsNothing()
    {
        // Arrange
        OutgoingMessage[] batch = [TestMessages.AnOutgoingMessage(tenant: _t1.Tenant), TestMessages.AnOutgoingMessage(tenant: new TenantId("t2"))];

        // Act
        var error = await Store().For(_t1).AddAsync(batch, Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, error?.Type);
        Assert.Empty(_database.InsertsOf(_outbox));
    }

    [Fact]
    public async Task AddAsync_BatchOfTwoMessages_InsertsBothDocumentsInOneCallWithNoTries()
    {
        // Arrange
        OutgoingMessage[] batch = [TestMessages.AnOutgoingMessage(tenant: _t1.Tenant), TestMessages.AnOutgoingMessage(tenant: _t1.Tenant)];

        // Act
        await Store().For(_t1).AddAsync(batch, Ct);

        // Assert
        var inserted = Assert.Single(_database.InsertsOf(_outbox));
        Assert.Equal(
            [(batch[0].Delivery.Id.ToString("D"), new BsonArray()), (batch[1].Delivery.Id.ToString("D"), new BsonArray())],
            inserted.Select(static document => (document["delivery"]["id"].AsString, document["tries"].AsBsonArray)));
    }

    [Fact]
    public async Task CountAsync_GroupOfAChannel_GivesTheCountWithTheChannel()
    {
        // Arrange
        _database.Aggregates(
            _outbox,
            BsonDocument.Parse("{ _id: { type: 'TestEvent', channel: 'Email', status: 'Failed', test: false }, count: 3, oldest: null }"));

        // Act
        var counts = await Store().For(_t1).CountAsync(new TrafficQuery(), Ct);

        // Assert
        Assert.Equal(
            [new MessageCount(new MessageType("TestEvent"), _email, DeliveryStatus.Failed, Test: false, Count: 3, OldestDueAtUtc: null)],
            counts.Value);
    }

    [Fact]
    public async Task SetChannelDownAsync_ChannelWithNoState_InsertsTheDownDocumentWithEachField()
    {
        // Arrange
        var down = new ChannelDown(_email, ChannelDownReason.Errors, _from, NextCheckAtUtc: _to, ErrorCode: 503, ErrorName: "Unavailable", Note: "note");

        // Act
        await Store().SetChannelDownAsync(down, Ct);

        // Assert
        Assert.Equal(BsonDocument.Parse(_downDocument).ToJson(), WithNoId(Assert.Single(StateInserts())).ToJson());
    }

    [Fact]
    public async Task SetChannelUpAsync_DownChannel_InsertsTheUpDocumentWithThePreviousKindAndDuration()
    {
        // Arrange
        _database.Finds(_states, BsonDocument.Parse(_downDocument));

        // Act
        await Store().SetChannelUpAsync(_email, reason: null, _to, Ct);

        // Assert
        Assert.Equal(BsonDocument.Parse(_upDocument).ToJson(), WithNoId(Assert.Single(StateInserts())).ToJson());
    }

    [Fact]
    public async Task GetChannelStatesAsync_UpDocument_GivesTheUpStateWithThePreviousKindAndDuration()
    {
        // Arrange
        _database.Finds(_states, BsonDocument.Parse(_upDocument));

        // Act
        var states = await Store().GetChannelStatesAsync(_from, Ct);

        // Assert
        Assert.Equal(
            [new ChannelState(_email, 2, ChannelStateKind.Up, _to, ChannelStateKind.Down, TimeSpan.FromDays(1), null, null, null, null, null)],
            states.Value);
    }

    [Fact]
    public async Task GetDownChannelsAsync_DownDocument_GivesTheDownChannelWithEachField()
    {
        // Arrange
        _database.Aggregates(_states, BsonDocument.Parse(_downDocument));

        // Act
        var downs = await Store().GetDownChannelsAsync(Ct);

        // Assert
        Assert.Equal(
            [new ChannelDown(_email, ChannelDownReason.Errors, _from, NextCheckAtUtc: _to, ErrorCode: 503, ErrorName: "Unavailable", Note: "note")],
            downs.Value);
    }

    private MongoDbOutboxStore Store() => new(_database.Database, new MsgHatchJson(TestContentTypes.ARegistry()));

    private IReadOnlyList<BsonDocument> StateInserts() =>
        [.. _database.CallsOf(_states, "InsertOneAsync").Select(static arguments => (BsonDocument)arguments[0]!)];

    private static BsonDocument WithNoId(BsonDocument document) => new(document.Where(static element => element.Name != "_id"));
}
