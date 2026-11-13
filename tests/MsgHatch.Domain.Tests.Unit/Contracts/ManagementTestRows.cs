using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The query scenario of the management tests of the store base classes (section 5.5), and the rows of the data-driven
/// tests. A generic class has no static members (CA1000). Of the tenant t1: A is pending and due before the start, B
/// failed on the second channel, and X is a pending test message. D is a failed message of the tenant t2.
/// </summary>
public static class ManagementTestRows
{
    public static readonly DateTime Start = TestMessages.CreatedAtUtc;

    public static readonly Guid A = new("0000000a-0000-0000-0000-000000000000");
    public static readonly Guid B = new("0000000b-0000-0000-0000-000000000000");
    public static readonly Guid X = new("0000000c-0000-0000-0000-000000000000");
    public static readonly Guid D = new("0000000d-0000-0000-0000-000000000000");
    public static readonly Guid SourceOfA = new("000000aa-0000-0000-0000-000000000000");
    public static readonly Guid SourceOfB = new("000000bb-0000-0000-0000-000000000000");

    /// <summary>Two ids for one creation time: their order is <c>f…</c> before <c>1…</c> in each store (D112).</summary>
    public static readonly Guid HighId = new("f0000000-0000-0000-0000-000000000000");
    public static readonly Guid LowId = new("10000000-0000-0000-0000-000000000000");

    public static readonly MessageType OtherType = new("OtherEvent");

    private static readonly Dictionary<string, (OutboxQuery Query, Guid[] Expected)> _outbox = new()
    {
        ["no filter"] = (new OutboxQuery(), [B, A]),
        ["ids"] = (new OutboxQuery(Ids: [A, D]), [A]),
        ["status"] = (new OutboxQuery(Status: DeliveryStatus.Failed), [B]),
        ["type"] = (new OutboxQuery(Type: OtherType), [B]),
        ["channel"] = (new OutboxQuery(Channel: TestChannel.SecondId), [B]),
        ["target"] = (new OutboxQuery(Target: new DeliveryTarget(DeliveryTargetKind.User, "u2")), [B]),
        ["source"] = (new OutboxQuery(SourceMessageId: SourceOfB), [B]),
        ["created from"] = (new OutboxQuery(CreatedFromUtc: Start.AddHours(-1)), [B]),
        ["created to"] = (new OutboxQuery(CreatedToUtc: Start.AddHours(-1)), [A]),
        ["due before"] = (new OutboxQuery(DueBeforeUtc: Start), [A]),
        ["include test"] = (new OutboxQuery(IncludeTest: true), [X, B, A]),
        ["combination"] = (new OutboxQuery(Status: DeliveryStatus.Pending, Channel: TestChannel.Id, IncludeTest: true), [X, A]),
    };

    private static readonly Dictionary<string, (InboxQuery Query, Guid[] Expected)> _inbox = new()
    {
        ["no filter"] = (new InboxQuery(), [B, A]),
        ["ids"] = (new InboxQuery(Ids: [A, D]), [A]),
        ["status"] = (new InboxQuery(Status: DeliveryStatus.Failed), [B]),
        ["type"] = (new InboxQuery(Type: new MessageType("TestEvent")), [B, A]),
        ["other type"] = (new InboxQuery(Type: OtherType), []),
        ["created from"] = (new InboxQuery(CreatedFromUtc: Start.AddHours(-1)), [B]),
        ["created to"] = (new InboxQuery(CreatedToUtc: Start.AddHours(-1)), [A]),
        ["due before"] = (new InboxQuery(DueBeforeUtc: Start), [A]),
        ["include test"] = (new InboxQuery(IncludeTest: true), [X, B, A]),
        ["combination"] = (new InboxQuery(Status: DeliveryStatus.Pending, IncludeTest: true), [X, A]),
    };

    public static TheoryData<string> OutboxQueryRows => [.. _outbox.Keys];

    public static TheoryData<string> InboxQueryRows => [.. _inbox.Keys];

    public static TheoryData<DeliveryStatus, int, DeliveryStatus> RetryStatusRows => new()
    {
        { DeliveryStatus.Failed, 1, DeliveryStatus.Pending },
        { DeliveryStatus.Pending, 1, DeliveryStatus.Pending },
        { DeliveryStatus.Done, 0, DeliveryStatus.Done },
        { DeliveryStatus.Cancelled, 0, DeliveryStatus.Cancelled },
    };

    public static TheoryData<DeliveryStatus, int, DeliveryStatus> CancelStatusRows => new()
    {
        { DeliveryStatus.Pending, 1, DeliveryStatus.Cancelled },
        { DeliveryStatus.Failed, 0, DeliveryStatus.Failed },
        { DeliveryStatus.Done, 0, DeliveryStatus.Done },
        { DeliveryStatus.Cancelled, 0, DeliveryStatus.Cancelled },
    };

    public static (OutboxQuery Query, Guid[] Expected) OutboxRow(string name) => _outbox[name];

    public static (InboxQuery Query, Guid[] Expected) InboxRow(string name) => _inbox[name];
}
