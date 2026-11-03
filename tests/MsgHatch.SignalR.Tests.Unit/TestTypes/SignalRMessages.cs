using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.SignalR.Tests.Unit.TestTypes;

/// <summary>Shared by the SignalR tests: a valid outgoing SignalR message, with named parameters for the values that tests vary.</summary>
public static class SignalRMessages
{
    public static OutgoingMessage<SignalREnvelope<TPayload>> AnOutgoingMessage<TPayload>(
        TPayload payload, TenantId? tenant = null, DeliveryTarget? target = null, MessageFlags flags = MessageFlags.None)
        where TPayload : class =>
        new(
            SchemaVersion: 1, TestMessages.ADeliveryState(), SourceMessageId: Guid.NewGuid(), tenant, new MessageType("TestEvent"),
            SignalRChannel.Id, target ?? new DeliveryTarget(DeliveryTargetKind.User, "u1"), "SignalR:TestUpdate", ContentVersion: 1, flags,
            TraceParent: null, Trace: null, new SignalREnvelope<TPayload>(payload));

    public static OutgoingMessage<SignalREnvelope<TestPayload>> AnOutgoingMessage(
        TenantId? tenant = null, DeliveryTarget? target = null, MessageFlags flags = MessageFlags.None) =>
        AnOutgoingMessage(new TestPayload("text"), tenant, target, flags);
}
