using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>Shared by the email tests: a valid received message, with named parameters for the values that tests vary.</summary>
public static class TestMessages
{
    public static readonly DateTime CreatedAtUtc = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static ReceivedMessage<TEvent> AReceivedMessage<TEvent>(
        TEvent domainEvent, TenantId? tenant = null, MessageFlags flags = MessageFlags.None) =>
        new(
            SchemaVersion: 1,
            new DeliveryState(
                Guid.NewGuid(), DeliveryStatus.Pending, TryCount: 0, MaxTryCount: 8, CreatedAtUtc, LastTryAtUtc: null,
                NextTryAtUtc: CreatedAtUtc, LeaseUntilUtc: null, ExpiresAtUtc: null, LastError: null),
            tenant, new MessageType("TestEvent"), ContentVersion: 1, flags, TraceParent: null, domainEvent);
}
