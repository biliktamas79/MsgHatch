using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Management;

/// <summary>The stateless rules of a delivery request (D292), with a valid row and an invalid row at each limit.</summary>
public sealed class DeliveryRulesUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));
    private static readonly DateTime _now = TestHost.Start.UtcDateTime;

    public static TheoryData<string, bool> RuleRows => new()
    {
        { "inbox limit 0", false },
        { "inbox limit 1", true },
        { "inbox limit 500", true },
        { "inbox limit 501", false },
        { "outbox limit 0", false },
        { "outbox limit 500", true },
        { "outbox limit 501", false },
        { "inbox ids 0", false },
        { "inbox ids 1", true },
        { "inbox ids 1000", true },
        { "inbox ids 1001", false },
        { "outbox ids 0", false },
        { "outbox ids 1000", true },
        { "outbox ids 1001", false },
        { "traffic created from at created to", false },
        { "traffic created from before created to", true },
        { "traffic created from only", true },
        { "inbox created from after created to", false },
        { "outbox created from at created to", false },
        { "outbox created from before created to", true },
        { "inbox bulk action with only limit, cursor, and test flag", false },
        { "inbox bulk action with a status", true },
        { "inbox bulk action with limit 0", false },
        { "outbox bulk action with only limit, cursor, and test flag", false },
        { "outbox bulk action with a channel", true },
        { "outbox bulk action with ids 0", false },
        { "pause with no note", true },
        { "pause note 500", true },
        { "pause note 501", false },
        { "pause of a tenant", false },
        { "global action of no tenant", true },
        { "global action of a tenant", false },
        { "purge max count 0", false },
        { "purge max count 1", true },
        { "purge of a tenant", false },
    };

    public static TheoryData<string> OutboxFilterRows =>
    [
        "ids", "status", "type", "channel", "target", "source message", "created from", "created to", "due before",
    ];

    public static TheoryData<string> InboxFilterRows => ["ids", "status", "type", "created from", "created to", "due before"];

    [Theory]
    [MemberData(nameof(RuleRows))]
    public void Check_Rule_GivesValidationFailedOnlyWhenInvalid(string row, bool valid)
    {
        // Act
        var error = Check(row);

        // Assert
        Assert.Equal((row, valid ? null : Errors.ValidationFailed), (row, error?.Type));
    }

    [Theory]
    [MemberData(nameof(OutboxFilterRows))]
    public void CheckBulkAction_OutboxQueryWithOneFilter_GivesNoError(string filter)
    {
        // Act
        var error = DeliveryRules.CheckBulkAction(OutboxQueryWith(filter));

        // Assert
        Assert.Equal((filter, (Error?)null), (filter, error));
    }

    [Theory]
    [MemberData(nameof(InboxFilterRows))]
    public void CheckBulkAction_InboxQueryWithOneFilter_GivesNoError(string filter)
    {
        // Act
        var error = DeliveryRules.CheckBulkAction(InboxQueryWith(filter));

        // Assert
        Assert.Equal((filter, (Error?)null), (filter, error));
    }

    private static Error? Check(string row) => row switch
    {
        "inbox limit 0" => DeliveryRules.Check(new InboxQuery(Limit: 0)),
        "inbox limit 1" => DeliveryRules.Check(new InboxQuery(Limit: 1)),
        "inbox limit 500" => DeliveryRules.Check(new InboxQuery(Limit: 500)),
        "inbox limit 501" => DeliveryRules.Check(new InboxQuery(Limit: 501)),
        "outbox limit 0" => DeliveryRules.Check(new OutboxQuery(Limit: 0)),
        "outbox limit 500" => DeliveryRules.Check(new OutboxQuery(Limit: 500)),
        "outbox limit 501" => DeliveryRules.Check(new OutboxQuery(Limit: 501)),
        "inbox ids 0" => DeliveryRules.Check(new InboxQuery(Ids: Ids(0))),
        "inbox ids 1" => DeliveryRules.Check(new InboxQuery(Ids: Ids(1))),
        "inbox ids 1000" => DeliveryRules.Check(new InboxQuery(Ids: Ids(1000))),
        "inbox ids 1001" => DeliveryRules.Check(new InboxQuery(Ids: Ids(1001))),
        "outbox ids 0" => DeliveryRules.Check(new OutboxQuery(Ids: Ids(0))),
        "outbox ids 1000" => DeliveryRules.Check(new OutboxQuery(Ids: Ids(1000))),
        "outbox ids 1001" => DeliveryRules.Check(new OutboxQuery(Ids: Ids(1001))),
        "traffic created from at created to" => DeliveryRules.Check(new TrafficQuery(_now, _now)),
        "traffic created from before created to" => DeliveryRules.Check(new TrafficQuery(_now.AddTicks(-1), _now)),
        "traffic created from only" => DeliveryRules.Check(new TrafficQuery(CreatedFromUtc: _now)),
        "inbox created from after created to" => DeliveryRules.Check(new InboxQuery(CreatedFromUtc: _now, CreatedToUtc: _now.AddTicks(-1))),
        "outbox created from at created to" => DeliveryRules.Check(new OutboxQuery(CreatedFromUtc: _now, CreatedToUtc: _now)),
        "outbox created from before created to" => DeliveryRules.Check(new OutboxQuery(CreatedFromUtc: _now.AddTicks(-1), CreatedToUtc: _now)),
        "inbox bulk action with only limit, cursor, and test flag" =>
            DeliveryRules.CheckBulkAction(new InboxQuery(IncludeTest: true, Limit: 10, After: new MessageCursor(_now, Guid.NewGuid()))),
        "inbox bulk action with a status" => DeliveryRules.CheckBulkAction(new InboxQuery(Status: DeliveryStatus.Failed)),
        "inbox bulk action with limit 0" => DeliveryRules.CheckBulkAction(new InboxQuery(Status: DeliveryStatus.Failed, Limit: 0)),
        "outbox bulk action with only limit, cursor, and test flag" =>
            DeliveryRules.CheckBulkAction(new OutboxQuery(IncludeTest: true, Limit: 10, After: new MessageCursor(_now, Guid.NewGuid()))),
        "outbox bulk action with a channel" => DeliveryRules.CheckBulkAction(new OutboxQuery(Channel: TestChannel.Id)),
        "outbox bulk action with ids 0" => DeliveryRules.CheckBulkAction(new OutboxQuery(Ids: Ids(0))),
        "pause with no note" => DeliveryRules.CheckPause(TenantKey.None, note: null),
        "pause note 500" => DeliveryRules.CheckPause(TenantKey.None, new string('n', 500)),
        "pause note 501" => DeliveryRules.CheckPause(TenantKey.None, new string('n', 501)),
        "pause of a tenant" => DeliveryRules.CheckPause(_t1, note: null),
        "global action of no tenant" => DeliveryRules.CheckGlobalAction(TenantKey.None),
        "global action of a tenant" => DeliveryRules.CheckGlobalAction(_t1),
        "purge max count 0" => DeliveryRules.CheckPurge(TenantKey.None, 0),
        "purge max count 1" => DeliveryRules.CheckPurge(TenantKey.None, 1),
        "purge of a tenant" => DeliveryRules.CheckPurge(_t1, 10),
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "No rule has this row."),
    };

    private static OutboxQuery OutboxQueryWith(string filter) => filter switch
    {
        "ids" => new OutboxQuery(Ids: Ids(1)),
        "status" => new OutboxQuery(Status: DeliveryStatus.Pending),
        "type" => new OutboxQuery(Type: new MessageType("TestEvent")),
        "channel" => new OutboxQuery(Channel: TestChannel.Id),
        "target" => new OutboxQuery(Target: new DeliveryTarget(DeliveryTargetKind.User, "1")),
        "source message" => new OutboxQuery(SourceMessageId: Guid.NewGuid()),
        "created from" => new OutboxQuery(CreatedFromUtc: _now),
        "created to" => new OutboxQuery(CreatedToUtc: _now),
        _ => new OutboxQuery(DueBeforeUtc: _now),
    };

    private static InboxQuery InboxQueryWith(string filter) => filter switch
    {
        "ids" => new InboxQuery(Ids: Ids(1)),
        "status" => new InboxQuery(Status: DeliveryStatus.Pending),
        "type" => new InboxQuery(Type: new MessageType("TestEvent")),
        "created from" => new InboxQuery(CreatedFromUtc: _now),
        "created to" => new InboxQuery(CreatedToUtc: _now),
        _ => new InboxQuery(DueBeforeUtc: _now),
    };

    private static Guid[] Ids(int count) => [.. Enumerable.Range(0, count).Select(static _ => Guid.NewGuid())];
}
