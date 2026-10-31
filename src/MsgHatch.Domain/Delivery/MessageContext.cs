using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Delivery;

/// <summary>The input of the caller for one event (D115).</summary>
/// <param name="MessageId">The id of the event, for example the broker message id (D20).</param>
/// <param name="Tenant">The tenant of the event, or null for no tenant.</param>
/// <param name="Flags">The flags of the event.</param>
public readonly record struct MessageContext(Guid MessageId, TenantId? Tenant = null, MessageFlags Flags = MessageFlags.None);
