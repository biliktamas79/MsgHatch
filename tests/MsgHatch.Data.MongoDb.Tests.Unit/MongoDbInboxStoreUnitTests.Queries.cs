using MongoDB.Bson;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.MongoDb.Tests.Unit;

/// <summary>The filters of a query and of a traffic count (D112, D164), the page of a query, and the counts of the groups.</summary>
public sealed partial class MongoDbInboxStoreUnitTests
{
    private static readonly DateTime _from = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime _to = new(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Guid _first = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid _second = new("22222222-2222-2222-2222-222222222222");

    public static TheoryData<string, InboxQuery, string> QueryFilterRows => new()
    {
        { "no filter", new InboxQuery(), "{ tenant: 't1', flags: { $ne: 'Test' } }" },
        { "the test messages too", new InboxQuery(IncludeTest: true), "{ tenant: 't1' }" },
        {
            "two ids", new InboxQuery(Ids: [_first, _second]),
            "{ tenant: 't1', 'delivery.id': { $in: ['11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222'] }, flags: { $ne: 'Test' } }"
        },
        { "a status", new InboxQuery(Status: DeliveryStatus.Failed), "{ tenant: 't1', 'delivery.status': 'Failed', flags: { $ne: 'Test' } }" },
        {
            "a created range", new InboxQuery(CreatedFromUtc: _from, CreatedToUtc: _to),
            "{ tenant: 't1', 'delivery.createdAtUtc.t': { $gte: NumberLong('640290528000000000'), $lt: NumberLong('640291392000000000') }, flags: { $ne: 'Test' } }"
        },
        {
            "a due time", new InboxQuery(DueBeforeUtc: _to),
            "{ tenant: 't1', 'delivery.status': 'Pending', 'delivery.nextTryAtUtc.t': { $lt: NumberLong('640291392000000000') }, flags: { $ne: 'Test' } }"
        },
        { "a message type", new InboxQuery(Type: new MessageType("TestEvent")), "{ tenant: 't1', flags: { $ne: 'Test' }, type: 'TestEvent' }" },
        {
            "the cursor of a page", new InboxQuery(After: new MessageCursor(_from, _first)),
            "{ tenant: 't1', flags: { $ne: 'Test' }, $or: [{ 'delivery.createdAtUtc.t': { $lt: NumberLong('640290528000000000') } }, "
                + "{ 'delivery.createdAtUtc.t': NumberLong('640290528000000000'), 'delivery.id': { $lt: '11111111-1111-1111-1111-111111111111' } }] }"
        },
    };

    public static TheoryData<string, TrafficQuery, string> TrafficFilterRows => new()
    {
        { "no filter", new TrafficQuery(), "{ tenant: 't1', flags: { $ne: 'Test' } }" },
        { "the test messages too", new TrafficQuery(IncludeTest: true), "{ tenant: 't1' }" },
        {
            "a created range", new TrafficQuery(CreatedFromUtc: _from, CreatedToUtc: _to),
            "{ tenant: 't1', 'delivery.createdAtUtc.t': { $gte: NumberLong('640290528000000000'), $lt: NumberLong('640291392000000000') }, flags: { $ne: 'Test' } }"
        },
        { "a status", new TrafficQuery(Status: DeliveryStatus.Done), "{ tenant: 't1', 'delivery.status': 'Done', flags: { $ne: 'Test' } }" },
    };

    [Theory]
    [MemberData(nameof(QueryFilterRows))]
    public async Task QueryAsync_FiltersOfTheQuery_FindsWithTheFilterOfTheRow(string row, InboxQuery query, string expected)
    {
        // Act
        await Store().For(_t1).QueryAsync(query, Ct);

        // Assert
        var find = Assert.Single(_database.FindsOf(_inbox));
        Assert.Equal((row, BsonDocument.Parse(expected).ToJson()), (row, DoubledDatabase.Rendered(find.Filter).ToJson()));
    }

    [Fact]
    public async Task QueryAsync_Limit_FindsNewestFirstWithNoEventAndTheLimit()
    {
        // Arrange
        var query = new InboxQuery(Limit: 2);

        // Act
        await Store().For(_t1).QueryAsync(query, Ct);

        // Assert
        var options = Assert.Single(_database.FindsOf(_inbox)).Options;
        Assert.Equal(
            ("{ \"delivery.createdAtUtc.t\" : -1, \"delivery.id\" : -1 }", "{ \"event\" : 0 }", (int?)2),
            (DoubledDatabase.Rendered(options.Sort).ToJson(), DoubledDatabase.Rendered(options.Projection).ToJson(), options.Limit));
    }

    [Fact]
    public async Task QueryAsync_PageWithTheLimitOfItems_GivesTheCursorOfTheLastItem()
    {
        // Arrange
        var store = Store();
        var (first, second) = (AMessage(), AMessage());
        await store.For(_t1).AddAsync(first, Ct);
        await store.For(_t1).AddAsync(second, Ct);
        _database.Finds(_inbox, [.. _database.InsertsOf(_inbox).Select(Assert.Single)]);

        // Act
        var page = await store.For(_t1).QueryAsync(new InboxQuery(Limit: 2), Ct);

        // Assert
        Assert.Equal([first.Delivery.Id, second.Delivery.Id], page.Value.Items.Select(static item => item.Delivery.Id));
        Assert.Equal(MessageCursor.Of(second.Delivery), page.Value.Next);
    }

    [Fact]
    public async Task QueryAsync_PageWithFewerItemsThanTheLimit_GivesNoCursor()
    {
        // Arrange
        var store = Store();
        await store.For(_t1).AddAsync(AMessage(), Ct);
        _database.Finds(_inbox, Assert.Single(Assert.Single(_database.InsertsOf(_inbox))));

        // Act
        var page = await store.For(_t1).QueryAsync(new InboxQuery(Limit: 2), Ct);

        // Assert
        Assert.Equal((1, (MessageCursor?)null), (page.Value.Items.Count, page.Value.Next));
    }

    [Theory]
    [MemberData(nameof(TrafficFilterRows))]
    public async Task CountAsync_FiltersOfTheQuery_MatchesTheFilterOfTheRow(string row, TrafficQuery query, string expected)
    {
        // Act
        await Store().For(_t1).CountAsync(query, Ct);

        // Assert
        var stages = Assert.Single(_database.AggregationsOf(_inbox));
        Assert.Equal((row, new BsonDocument("$match", BsonDocument.Parse(expected)).ToJson()), (row, stages[0].ToJson()));
    }

    [Fact]
    public async Task CountAsync_TwoGroups_GivesTheCountOfEachGroupWithItsEarliestDueTime()
    {
        // Arrange
        _database.Aggregates(
            _inbox,
            BsonDocument.Parse("{ _id: { type: 'TestEvent', status: 'Pending', test: false }, count: 2, oldest: NumberLong('640290528000000000') }"),
            BsonDocument.Parse("{ _id: { type: 'TestEvent', status: 'Done', test: true }, count: 1, oldest: null }"));

        // Act
        var counts = await Store().For(_t1).CountAsync(new TrafficQuery(IncludeTest: true), Ct);

        // Assert
        Assert.Equal(
            [
                new MessageCount(new MessageType("TestEvent"), Channel: null, DeliveryStatus.Pending, Test: false, Count: 2, _from),
                new MessageCount(new MessageType("TestEvent"), Channel: null, DeliveryStatus.Done, Test: true, Count: 1, OldestDueAtUtc: null),
            ],
            counts.Value);
    }
}
