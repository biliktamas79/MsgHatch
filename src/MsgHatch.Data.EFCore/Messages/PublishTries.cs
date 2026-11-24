using System.Buffers;
using System.Text;
using System.Text.Json;
using MsgHatch.Domain.Delivery;

namespace MsgHatch.Data.EFCore.Messages;

/// <summary>The JSON array <c>Tries</c> of an outgoing message (section 4.5, D27), which the store writes in C# (D318).</summary>
internal static class PublishTries
{
    /// <summary>The array of the stored tries with one more try at its end.</summary>
    /// <exception cref="JsonException">The stored tries are not a JSON array.</exception>
    public static string Append(string storedTries, PublishTry publishTry)
    {
        using var stored = JsonDocument.Parse(storedTries);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var element in stored.RootElement.EnumerateArray())
            {
                element.WriteTo(writer);
            }

            WriteTry(writer, publishTry);
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteTry(Utf8JsonWriter writer, PublishTry publishTry)
    {
        writer.WriteStartObject();
        writer.WriteNumber("tryNumber", publishTry.TryNumber);
        writer.WriteString("startedAtUtc", MessageDocuments.Time(publishTry.StartedAtUtc));
        writer.WriteString("finishedAtUtc", MessageDocuments.Time(publishTry.FinishedAtUtc));
        writer.WriteString("outcome", publishTry.Outcome.ToString());
        writer.WriteString("providerMessageId", publishTry.ProviderMessageId);
        if (publishTry.ErrorCode is { } code)
        {
            writer.WriteNumber("errorCode", code);
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
