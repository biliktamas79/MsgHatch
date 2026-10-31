using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Delivery;

/// <summary>The outbox envelope of one message on one channel.</summary>
/// <typeparam name="TEnvelope">The type of the channel envelope.</typeparam>
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
/// <param name="Envelope">The channel envelope.</param>
public sealed record OutgoingMessage<TEnvelope>(
    int SchemaVersion, DeliveryState Delivery, Guid SourceMessageId, TenantId? Tenant, MessageType Type,
    ChannelId Channel, DeliveryTarget Target, string ContentType, int ContentVersion, MessageFlags Flags,
    string? TraceParent, RouteTrace? Trace, TEnvelope Envelope)
    : OutgoingMessage(
        SchemaVersion, Delivery, SourceMessageId, Tenant, Type, Channel, Target, ContentType, ContentVersion,
        Flags, TraceParent, Trace)
    where TEnvelope : class
{
    /// <inheritdoc />
    public override object Content => Envelope;
}
