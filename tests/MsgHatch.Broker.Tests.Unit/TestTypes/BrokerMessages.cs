using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Broker.Tests.Unit.TestTypes;

/// <summary>Shared by the broker tests: a valid outgoing broker message, with named parameters for the values that tests vary.</summary>
public static class BrokerMessages
{
    public static OutgoingMessage<BrokerEnvelope<TPayload>> AnOutgoingMessage<TPayload>(
        TPayload payload, Guid? id = null, TenantId? tenant = null, MessageFlags flags = MessageFlags.None,
        IReadOnlyDictionary<string, string>? headers = null)
        where TPayload : class =>
        new(
            SchemaVersion: 1, TestMessages.ADeliveryState(id), SourceMessageId: Guid.NewGuid(), tenant, new MessageType("TestEvent"),
            BrokerChannel.Id, new DeliveryTarget(DeliveryTargetKind.Company, "c1"), "Broker:TestMessage", ContentVersion: 1, flags,
            TraceParent: null, Trace: null, new BrokerEnvelope<TPayload>(payload, headers));

    public static OutgoingMessage<BrokerEnvelope<TestPayload>> AnOutgoingMessage(
        Guid? id = null, TenantId? tenant = null, MessageFlags flags = MessageFlags.None, IReadOnlyDictionary<string, string>? headers = null) =>
        AnOutgoingMessage(new TestPayload("text"), id, tenant, flags, headers);
}
