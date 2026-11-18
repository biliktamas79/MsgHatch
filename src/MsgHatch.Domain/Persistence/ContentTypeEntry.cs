using System.Text.Json.Serialization.Metadata;
using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Persistence;

/// <summary>One registered version of a persisted type (section 4.5).</summary>
/// <param name="ContentType">
/// The content type: the message type name for an event, <c>{Channel}:{Name}</c> for an envelope,
/// <c>Settings:{Channel}</c> for channel settings, and <c>Trace:{Name}</c> for a route trace.
/// </param>
/// <param name="Version">The version.</param>
/// <param name="Discriminator">The stable discriminator <c>{ContentType}@{Version}</c> (D30).</param>
/// <param name="ClrType">
/// The closed type that a store reads: <c>ReceivedMessage&lt;TEvent&gt;</c> for an event,
/// <c>OutgoingMessage&lt;TEnvelope&gt;</c> for an envelope, and the type itself for settings and traces.
/// </param>
/// <param name="ContentJsonTypeInfo">The JSON type information of the event, envelope, settings, or trace.</param>
public sealed record ContentTypeEntry(
    string ContentType, int Version, string Discriminator, Type ClrType, JsonTypeInfo ContentJsonTypeInfo)
{
    /// <summary>The typed code of a message entry, which <see cref="MsgHatchJson"/> uses, or null for settings and traces (D237).</summary>
    internal MessageShape? Shape { get; init; }

    /// <summary>Gives the entry of a received message of <typeparamref name="TEvent"/>, with the discriminator <c>{ContentType}@{Version}</c>.</summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <param name="contentType">The content type, the message type name.</param>
    /// <param name="version">The version, at least 1.</param>
    /// <param name="json">The JSON type information of the domain event.</param>
    /// <returns>The entry.</returns>
    public static ContentTypeEntry ForReceivedMessage<TEvent>(string contentType, int version, JsonTypeInfo<TEvent> json) =>
        ContentTypeRegistry.Entry(contentType, version, typeof(ReceivedMessage<TEvent>), json) with { Shape = ReceivedShape<TEvent>.Instance };

    /// <summary>Gives the entry of an outgoing message of <typeparamref name="TEnvelope"/>, with the discriminator <c>{ContentType}@{Version}</c>.</summary>
    /// <typeparam name="TEnvelope">The envelope type.</typeparam>
    /// <param name="contentType">The content type, <c>{Channel}:{Name}</c>.</param>
    /// <param name="version">The version, at least 1.</param>
    /// <param name="json">The JSON type information of the envelope.</param>
    /// <returns>The entry.</returns>
    public static ContentTypeEntry ForOutgoingMessage<TEnvelope>(string contentType, int version, JsonTypeInfo<TEnvelope> json)
        where TEnvelope : class =>
        ContentTypeRegistry.Entry(contentType, version, typeof(OutgoingMessage<TEnvelope>), json) with { Shape = OutgoingShape<TEnvelope>.Instance };
}
