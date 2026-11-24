using System.Text.Json;
using MsgHatch.Domain.Delivery;

namespace MsgHatch.Data.EFCore.Messages;

/// <summary>
/// Splits a message into the columns of its row in C# (D176, D318): the delivery state, the versions, and the flags from
/// the message, and the event, the envelope, and the route trace with its <c>$type</c> as JSON from the document that
/// <c>MsgHatchJson</c> wrote.
/// </summary>
internal static class MessageRows
{
    /// <exception cref="JsonException">The document is not JSON.</exception>
    public static InboxRow Received(ReceivedMessage message, byte[] document)
    {
        using var json = JsonDocument.Parse(document);
        return WithDelivery(
            new InboxRow
            {
                TenantId = MessageQueries.Tenant(message.Tenant), SchemaVersion = message.SchemaVersion, ContentType = message.Type.Name,
                ContentVersion = message.ContentVersion, Flags = message.Flags.ToString(), TraceParent = message.TraceParent,
                Content = json.RootElement.GetProperty("event").GetRawText(),
            },
            message.Delivery);
    }

    /// <exception cref="JsonException">The document is not JSON.</exception>
    public static OutboxRow Outgoing(OutgoingMessage message, byte[] document)
    {
        using var json = JsonDocument.Parse(document);
        var trace = json.RootElement.GetProperty("trace");
        var hasTrace = trace.ValueKind != JsonValueKind.Null;
        return WithDelivery(
            new OutboxRow
            {
                TenantId = MessageQueries.Tenant(message.Tenant), SchemaVersion = message.SchemaVersion,
                SourceMessageId = MessageQueries.Id(message.SourceMessageId), MessageType = message.Type.Name, Channel = message.Channel.Name,
                TargetKind = message.Target.Kind.ToString(), TargetId = message.Target.Id, ContentType = message.ContentType,
                ContentVersion = message.ContentVersion, Flags = message.Flags.ToString(), TraceParent = message.TraceParent,
                TraceType = hasTrace && trace.TryGetProperty("$type", out var type) ? type.GetString() : null,
                Trace = hasTrace ? trace.GetRawText() : null, Content = json.RootElement.GetProperty("envelope").GetRawText(), Tries = "[]",
            },
            message.Delivery);
    }

    private static TRow WithDelivery<TRow>(TRow row, DeliveryState delivery)
        where TRow : MessageRow
    {
        row.Id = MessageQueries.Id(delivery.Id);
        row.Status = delivery.Status.ToString();
        row.TryCount = delivery.TryCount;
        row.MaxTryCount = delivery.MaxTryCount;
        row.CreatedAtUtc = delivery.CreatedAtUtc;
        row.LastTryAtUtc = delivery.LastTryAtUtc;
        row.NextTryAtUtc = delivery.NextTryAtUtc;
        row.LeaseUntilUtc = delivery.LeaseUntilUtc;
        row.ExpiresAtUtc = delivery.ExpiresAtUtc;
        row.LastError = delivery.LastError;
        return row;
    }
}
