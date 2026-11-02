using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the email tests: a valid received message, a valid email, and a valid outgoing email, with named parameters
/// for the values that tests vary.
/// </summary>
public static class TestMessages
{
    public static readonly DateTime CreatedAtUtc = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static ReceivedMessage<TEvent> AReceivedMessage<TEvent>(
        TEvent domainEvent, TenantId? tenant = null, MessageFlags flags = MessageFlags.None) =>
        new(
            SchemaVersion: 1, ADeliveryState(), tenant, new MessageType("TestEvent"), ContentVersion: 1, flags, TraceParent: null,
            domainEvent);

    public static EmailMessage AnEmail(
        string to = "client1@example.com",
        string subject = "Order 1001 shipped",
        IReadOnlyList<string>? cc = null,
        IReadOnlyList<string>? bcc = null) =>
        new(to, subject, "<p>Order 1001</p>", "Order 1001", From: null, cc, bcc);

    public static OutgoingMessage<EmailMessage> AnOutgoingEmail(EmailMessage? email = null, Guid? id = null, TenantId? tenant = null) =>
        new(
            SchemaVersion: 1, ADeliveryState(id), SourceMessageId: Guid.NewGuid(), tenant, new MessageType("TestEvent"), EmailChannel.Id,
            new DeliveryTarget(DeliveryTargetKind.User, "u1"), "Email:OrderEmail", ContentVersion: 1, MessageFlags.None, TraceParent: null,
            Trace: null, email ?? AnEmail());

    private static DeliveryState ADeliveryState(Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(), DeliveryStatus.Pending, TryCount: 0, MaxTryCount: 8, CreatedAtUtc, LastTryAtUtc: null,
            NextTryAtUtc: CreatedAtUtc, LeaseUntilUtc: null, ExpiresAtUtc: null, LastError: null);
}
