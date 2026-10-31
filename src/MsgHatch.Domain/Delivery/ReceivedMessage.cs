using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Delivery;

/// <summary>The inbox envelope of one domain event, with no generic type, for the stores.</summary>
/// <param name="SchemaVersion">The version of the library contract. The current version is 1.</param>
/// <param name="Delivery">The delivery state.</param>
/// <param name="Tenant">The tenant, or null for no tenant.</param>
/// <param name="Type">The message type.</param>
/// <param name="ContentVersion">The contract version of the domain event.</param>
/// <param name="Flags">The flags.</param>
/// <param name="TraceParent">The W3C trace context of the span that made the message, or null (D132).</param>
public abstract record ReceivedMessage(
    int SchemaVersion, DeliveryState Delivery, TenantId? Tenant, MessageType Type, int ContentVersion,
    MessageFlags Flags, string? TraceParent);
