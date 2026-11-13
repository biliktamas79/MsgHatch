using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Management;

/// <summary>A received message with no content: no event and no payload value (D109, D112).</summary>
/// <param name="Delivery">The delivery state. <c>LastError</c> can contain the error message of a provider.</param>
/// <param name="Tenant">The tenant, or null for no tenant.</param>
/// <param name="Type">The message type.</param>
/// <param name="ContentVersion">The contract version of the domain event.</param>
/// <param name="Flags">The flags.</param>
public sealed record ReceivedMessageSummary(
    DeliveryState Delivery, TenantId? Tenant, MessageType Type, int ContentVersion, MessageFlags Flags)
{
    /// <summary>Gives the summary of a received message.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The summary.</returns>
    public static ReceivedMessageSummary Of(ReceivedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new(message.Delivery, message.Tenant, message.Type, message.ContentVersion, message.Flags);
    }
}
