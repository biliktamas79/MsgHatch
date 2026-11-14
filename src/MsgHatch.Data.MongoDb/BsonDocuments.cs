using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;

namespace MsgHatch.Data.MongoDb;

/// <summary>
/// Converts the JSON documents of <c>MsgHatchJson</c> into BSON documents with the same names, and back (D184). An
/// object becomes a BSON document, an array a BSON array, and a string, a boolean, or null its BSON value. A number
/// becomes a 32-bit or a 64-bit integer when it is one, and else a <see cref="Decimal128"/> with the digits of its text,
/// so the number round-trips unchanged. A number with more than the 34 significant digits of a <see cref="Decimal128"/>
/// gives a <see cref="FormatException"/>, because <see cref="Decimal128.TryParse(string, out Decimal128)"/> does not
/// round it (checked fact of phase 19 in the roadmap). A time is the sub-document <c>{ d, t }</c> (D185): <c>d</c> is the BSON date cut
/// to the millisecond, for the tools, and <c>t</c> is the number of UTC ticks, which a read takes. A BSON document with
/// only <c>d</c> as a date and <c>t</c> as a 64-bit integer goes back to JSON as the ISO 8601 text of the time. JSON has
/// no BSON date, so a document from JSON never has this shape.
/// </summary>
internal static class BsonDocuments
{
    /// <summary>The field of the exact time, the UTC ticks.</summary>
    public const string Ticks = "t";

    private const string _date = "d";

    private const string _timeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    /// <summary>Converts a UTF-8 JSON object into a BSON document.</summary>
    /// <exception cref="JsonException">The text is not a JSON object.</exception>
    public static BsonDocument FromJson(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        using var document = JsonDocument.ParseValue(ref reader);
        return DocumentOf(document.RootElement);
    }

    /// <summary>Converts a JSON text into its BSON value.</summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static BsonValue ValueOfJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ValueOf(document.RootElement);
    }

    /// <summary>Converts a BSON document into UTF-8 JSON, with no field of <paramref name="skipped"/> at its top level.</summary>
    /// <exception cref="FormatException">A value has a BSON type that JSON does not have, for example an ObjectId.</exception>
    public static byte[] ToJson(BsonDocument document, params ReadOnlySpan<string> skipped)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var element in document)
            {
                if (!skipped.Contains(element.Name))
                {
                    writer.WritePropertyName(element.Name);
                    Write(writer, element.Value);
                }
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Converts a BSON value into JSON text.</summary>
    /// <exception cref="FormatException">A value has a BSON type that JSON does not have.</exception>
    public static string ToJsonText(BsonValue value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, value);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A time as the sub-document <c>{ d, t }</c> (D185).</summary>
    public static BsonDocument Time(DateTime time) =>
        new()
        {
            { _date, new BsonDateTime((time.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond) },
            { Ticks, time.Ticks },
        };

    /// <summary>A time as the sub-document <c>{ d, t }</c>, or null.</summary>
    public static BsonValue Time(DateTime? time) => time is { } value ? Time(value) : BsonNull.Value;

    /// <summary>The exact time of a sub-document <c>{ d, t }</c>, with the kind UTC.</summary>
    /// <exception cref="InvalidCastException">The value is not a time document.</exception>
    /// <exception cref="KeyNotFoundException">The document has no <c>t</c>.</exception>
    public static DateTime TimeOf(BsonValue value) => new(value.AsBsonDocument[Ticks].AsInt64, DateTimeKind.Utc);

    /// <summary>The exact time of a sub-document <c>{ d, t }</c>, or null for a BSON null.</summary>
    public static DateTime? NullableTimeOf(BsonValue value) => value.IsBsonNull ? null : TimeOf(value);

    /// <summary>A time as ISO 8601 text in UTC with 7 fractional digits, as the publish tries and the documents keep it.</summary>
    public static string TimeText(DateTime time) => time.ToString(_timeFormat, CultureInfo.InvariantCulture);

    /// <summary>A text, or null.</summary>
    public static BsonValue Text(string? text) => text is null ? BsonNull.Value : new BsonString(text);

    /// <summary>A text value, or null for a BSON null.</summary>
    /// <exception cref="InvalidCastException">The value is not a text.</exception>
    public static string? NullableTextOf(BsonValue value) => value.IsBsonNull ? null : value.AsString;

    private static BsonDocument DocumentOf(JsonElement element)
    {
        var document = new BsonDocument();
        foreach (var property in element.EnumerateObject())
        {
            document.Add(property.Name, ValueOf(property.Value));
        }

        return document;
    }

    private static BsonValue ValueOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => DocumentOf(element),
        JsonValueKind.Array => new BsonArray(element.EnumerateArray().Select(ValueOf)),
        JsonValueKind.String => new BsonString(element.GetString()!),
        JsonValueKind.Number => NumberOf(element),
        JsonValueKind.True => BsonBoolean.True,
        JsonValueKind.False => BsonBoolean.False,
        _ => BsonNull.Value,
    };

    private static BsonValue NumberOf(JsonElement element) =>
        element.TryGetInt32(out var int32) ? new BsonInt32(int32)
        : element.TryGetInt64(out var int64) ? new BsonInt64(int64)
        : Decimal128.TryParse(element.GetRawText(), out var exact) ? new BsonDecimal128(exact)
        : throw new FormatException($"The JSON number {element.GetRawText()} has more digits than a BSON decimal.");

    private static void Write(Utf8JsonWriter writer, BsonValue value)
    {
        switch (value.BsonType)
        {
            case BsonType.Document when IsTime(value.AsBsonDocument):
                writer.WriteStringValue(TimeText(TimeOf(value)));
                break;
            case BsonType.Document:
                writer.WriteStartObject();
                foreach (var element in value.AsBsonDocument)
                {
                    writer.WritePropertyName(element.Name);
                    Write(writer, element.Value);
                }

                writer.WriteEndObject();
                break;
            case BsonType.Array:
                writer.WriteStartArray();
                foreach (var item in value.AsBsonArray)
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;
            case BsonType.String:
                writer.WriteStringValue(value.AsString);
                break;
            case BsonType.Int32:
                writer.WriteNumberValue(value.AsInt32);
                break;
            case BsonType.Int64:
                writer.WriteNumberValue(value.AsInt64);
                break;
            case BsonType.Double:
                writer.WriteNumberValue(value.AsDouble);
                break;
            case BsonType.Decimal128:
                writer.WriteRawValue(value.AsDecimal128.ToString());
                break;
            case BsonType.Boolean:
                writer.WriteBooleanValue(value.AsBoolean);
                break;
            case BsonType.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new FormatException($"The BSON type {value.BsonType} has no JSON value.");
        }
    }

    private static bool IsTime(BsonDocument document) =>
        document.ElementCount == 2
        && document.TryGetValue(_date, out var date) && date.IsBsonDateTime
        && document.TryGetValue(Ticks, out var ticks) && ticks.IsInt64;
}
