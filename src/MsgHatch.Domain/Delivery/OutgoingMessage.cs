using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Delivery;

/// <summary>The outbox envelope of one message on one channel, with no generic type, for the stores and the transports.</summary>
/// <param name="SchemaVersion">The version of the library contract. The current version is 1.</param>
/// <param name="Delivery">The delivery state.</param>
/// <param name="SourceMessageId">The id of the event that gave the message.</param>
/// <param name="Tenant">The tenant, or null for no tenant.</param>
/// <param name="Type">The message type of the event (D113).</param>
/// <param name="Channel">The channel.</param>
/// <param name="Target">The delivery target of the route (D113).</param>
/// <param name="ContentType">The content type of the channel envelope, for example <c>Email:OrderShippedEmail</c>.</param>
/// <param name="ContentVersion">The contract version of the channel envelope.</param>
/// <param name="Flags">The flags.</param>
/// <param name="TraceParent">The W3C trace context of the span that made the message, or null (D132).</param>
/// <param name="Trace">The trace of the route that gave the message, or null (D98).</param>
public abstract record OutgoingMessage(
    int SchemaVersion, DeliveryState Delivery, Guid SourceMessageId, TenantId? Tenant, MessageType Type,
    ChannelId Channel, DeliveryTarget Target, string ContentType, int ContentVersion, MessageFlags Flags,
    string? TraceParent, RouteTrace? Trace)
{
    /// <summary>The channel envelope as an object, so that a store and a transport need no generic type (D129).</summary>
    public abstract object Content { get; }
}
