using System.Text.Json.Serialization.Metadata;

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
    string ContentType, int Version, string Discriminator, Type ClrType, JsonTypeInfo ContentJsonTypeInfo);
