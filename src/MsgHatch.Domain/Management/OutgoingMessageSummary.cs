using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>An outgoing message with no content: no envelope, no address, and no payload value (D109, D112).</summary>
/// <param name="Delivery">The delivery state. <c>LastError</c> can contain the error message of a provider.</param>
/// <param name="SourceMessageId">The id of the event that gave the message.</param>
/// <param name="Tenant">The tenant, or null for no tenant.</param>
/// <param name="Type">The message type of the event.</param>
/// <param name="Channel">The channel.</param>
/// <param name="Target">The delivery target of the route.</param>
/// <param name="ContentType">The content type of the channel envelope.</param>
/// <param name="ContentVersion">The contract version of the channel envelope.</param>
/// <param name="Flags">The flags.</param>
public sealed record OutgoingMessageSummary(
    DeliveryState Delivery, Guid SourceMessageId, TenantId? Tenant, MessageType Type, ChannelId Channel,
    DeliveryTarget Target, string ContentType, int ContentVersion, MessageFlags Flags)
{
    /// <summary>Gives the summary of an outgoing message.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The summary.</returns>
    public static OutgoingMessageSummary Of(OutgoingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new(
            message.Delivery, message.SourceMessageId, message.Tenant, message.Type, message.Channel, message.Target,
            message.ContentType, message.ContentVersion, message.Flags);
    }
}
