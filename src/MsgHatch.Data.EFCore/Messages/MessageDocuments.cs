using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace MsgHatch.Data.EFCore.Messages;

/// <summary>
/// Puts the columns of a message row back into the document that <c>MsgHatchJson</c> reads (D143, D176). A time is
/// ISO 8601 text in UTC with 7 fractional digits and <c>Z</c>. A JSON column with invalid JSON goes into the document
/// as a JSON string, so an edited row gives the error of that row only.
/// </summary>
internal static class MessageDocuments
{
    private const string _timeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public static byte[] Received(InboxRow row) => Write(row, static (writer, row) =>
    {
        writer.WriteNumber("schemaVersion", row.SchemaVersion);
        Delivery(writer, row);
        Tenant(writer, row);
        writer.WriteString("type", row.ContentType);
        writer.WriteNumber("contentVersion", row.ContentVersion);
        writer.WriteString("flags", row.Flags);
        writer.WriteString("traceParent", row.TraceParent);
        Json(writer, "event", row.Content);
    });

    public static byte[] Outgoing(OutboxRow row) => Write(row, static (writer, row) =>
    {
        writer.WriteNumber("schemaVersion", row.SchemaVersion);
        Delivery(writer, row);
        writer.WriteString("sourceMessageId", row.SourceMessageId);
        Tenant(writer, row);
        writer.WriteString("type", row.MessageType);
        writer.WriteString("channel", row.Channel);
        writer.WriteStartObject("target");
        writer.WriteString("kind", row.TargetKind);
        writer.WriteString("id", row.TargetId);
        writer.WriteEndObject();
        writer.WriteString("contentType", row.ContentType);
        writer.WriteNumber("contentVersion", row.ContentVersion);
        writer.WriteString("flags", row.Flags);
        writer.WriteString("traceParent", row.TraceParent);
        Json(writer, "trace", row.Trace);
        Json(writer, "envelope", row.Content);
    });

    /// <summary>A time as ISO 8601 text in UTC, as the documents and the publish tries keep it.</summary>
    public static string Time(DateTime time) => time.ToString(_timeFormat, CultureInfo.InvariantCulture);

    private static byte[] Write<TRow>(TRow row, Action<Utf8JsonWriter, TRow> properties)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            properties(writer, row);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void Delivery(Utf8JsonWriter writer, MessageRow row)
    {
        writer.WriteStartObject("delivery");
        writer.WriteString("id", row.Id);
        writer.WriteString("status", row.Status);
        writer.WriteNumber("tryCount", row.TryCount);
        writer.WriteNumber("maxTryCount", row.MaxTryCount);
        TimeProperty(writer, "createdAtUtc", row.CreatedAtUtc);
        TimeProperty(writer, "lastTryAtUtc", row.LastTryAtUtc);
        TimeProperty(writer, "nextTryAtUtc", row.NextTryAtUtc);
        TimeProperty(writer, "leaseUntilUtc", row.LeaseUntilUtc);
        TimeProperty(writer, "expiresAtUtc", row.ExpiresAtUtc);
        writer.WriteString("lastError", row.LastError);
        writer.WriteEndObject();
    }

    private static void Tenant(Utf8JsonWriter writer, MessageRow row) =>
        writer.WriteString("tenant", row.TenantId.Length == 0 ? null : row.TenantId);

    private static void TimeProperty(Utf8JsonWriter writer, string name, DateTime? time) =>
        writer.WriteString(name, time is { } value ? Time(value) : null);

    private static void Json(Utf8JsonWriter writer, string name, string? json)
    {
        writer.WritePropertyName(name);
        if (json is null)
        {
            writer.WriteNullValue();
        }
        else if (IsJson(json))
        {
            writer.WriteRawValue(json, skipInputValidation: true);
        }
        else
        {
            writer.WriteStringValue(json);
        }
    }

    private static bool IsJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
