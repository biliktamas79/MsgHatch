using System.Buffers;
using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.FileSystem;

/// <summary>
/// The file of one outgoing message (section 4.5): the message as <see cref="MsgHatchJson"/> writes it, and the
/// property <c>tries</c> with the publish tries in the order of their record. <see cref="MsgHatchJson"/> ignores the
/// property <c>tries</c> when it reads the file.
/// </summary>
internal static class OutboxFile
{
    /// <summary>Writes a message with the tries of its old file, and a new try.</summary>
    /// <param name="json">The serializer.</param>
    /// <param name="message">The message.</param>
    /// <param name="oldContent">The old file, or null for a new message.</param>
    /// <param name="newTry">The new try, or null.</param>
    /// <returns>The UTF-8 JSON, or the error of the serializer.</returns>
    public static ErrorOr<byte[]> Write(MsgHatchJson json, OutgoingMessage message, byte[]? oldContent, PublishTry? newTry) =>
        json.Serialize(message).ThenIfSuccess(messageJson => ErrorOr<byte[]>.ForValue(WithTries(messageJson, oldContent, newTry)));

    private static byte[] WithTries(byte[] messageJson, byte[]? oldContent, PublishTry? newTry)
    {
        using var message = JsonDocument.Parse(messageJson);
        using var old = oldContent is null ? null : JsonDocument.Parse(oldContent);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in message.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteStartArray("tries");
            WriteOldTries(writer, old);
            if (newTry is not null)
            {
                WriteTry(writer, newTry);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteOldTries(Utf8JsonWriter writer, JsonDocument? old)
    {
        if (old is null || !old.RootElement.TryGetProperty("tries", out var tries) || tries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var oldTry in tries.EnumerateArray())
        {
            oldTry.WriteTo(writer);
        }
    }

    private static void WriteTry(Utf8JsonWriter writer, PublishTry publishTry)
    {
        writer.WriteStartObject();
        writer.WriteNumber("tryNumber", publishTry.TryNumber);
        writer.WriteString("startedAtUtc", publishTry.StartedAtUtc);
        writer.WriteString("finishedAtUtc", publishTry.FinishedAtUtc);
        writer.WriteString("outcome", publishTry.Outcome.ToString());
        writer.WriteString("providerMessageId", publishTry.ProviderMessageId);
        if (publishTry.ErrorCode is { } errorCode)
        {
            writer.WriteNumber("errorCode", errorCode);
        }
        else
        {
            writer.WriteNull("errorCode");
        }

        writer.WriteString("errorName", publishTry.ErrorName);
        writer.WriteString("errorMessage", publishTry.ErrorMessage);
        writer.WriteEndObject();
    }
}
