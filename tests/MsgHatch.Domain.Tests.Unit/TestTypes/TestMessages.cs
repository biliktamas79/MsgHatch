using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>Shared by the message, JSON, and store tests: valid messages with named parameters for the values that tests vary.</summary>
public static class TestMessages
{
    public static readonly DateTime CreatedAtUtc = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DeliveryState ADeliveryState(Guid? id = null, string? lastError = null) =>
        new(
            id ?? Guid.NewGuid(), DeliveryStatus.Pending, TryCount: 0, MaxTryCount: 8, CreatedAtUtc, LastTryAtUtc: null,
            NextTryAtUtc: CreatedAtUtc, LeaseUntilUtc: null, ExpiresAtUtc: null, lastError);

    public static OutgoingMessage<TestEnvelope> AnOutgoingMessage(
        TestEnvelope? envelope = null,
        Guid? id = null,
        TenantId? tenant = null,
        MessageFlags flags = MessageFlags.None,
        RouteTrace? trace = null,
        string targetId = "u1",
        int contentVersion = 1,
        DeliveryState? delivery = null) =>
        new(
            SchemaVersion: 1, delivery ?? ADeliveryState(id), SourceMessageId: Guid.NewGuid(), tenant,
            new MessageType("TestEvent"), new ChannelId("Test"), new DeliveryTarget(DeliveryTargetKind.User, targetId),
            "Test:TestEnvelope", contentVersion, flags, TraceParent: null, trace, envelope ?? new TestEnvelope("text"));

    public static ReceivedMessage<TEvent> AReceivedMessage<TEvent>(
        TEvent domainEvent, TenantId? tenant = null, int contentVersion = 2, MessageFlags flags = MessageFlags.None) =>
        new(
            SchemaVersion: 1, ADeliveryState(), tenant, new MessageType("TestEvent"), contentVersion, flags,
            TraceParent: null, domainEvent);
}
