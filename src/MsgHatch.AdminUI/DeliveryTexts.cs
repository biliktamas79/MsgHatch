using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AdminUI;

/// <summary>The texts of the delivery pages (D295 to D298).</summary>
internal static class DeliveryTexts
{
    /// <summary>The format of a time in an input of the type <c>datetime-local</c> and in an address, in UTC to the second.</summary>
    private const string _inputTimeFormat = "yyyy-MM-dd'T'HH:mm:ss";

    /// <summary>The text of a retry or a cancel of one message that changed nothing.</summary>
    private const string _noChange = "The message did not change, because it is done, cancelled, leased, or expired.";

    /// <summary>
    /// The options of the formatted JSON text. The page writes the text as HTML text, which escapes it, so the JSON text keeps
    /// each character that is not ASCII.
    /// </summary>
    private static readonly JsonWriterOptions _indented = new() { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Gives a UTC time to the second, for example "2030-01-01 10:00:05 UTC".</summary>
    public static string TimeOf(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Gives a UTC time to the second, or an empty text for no time.</summary>
    public static string TimeOf(DateTime? utc) => utc is { } time ? TimeOf(time) : string.Empty;

    /// <summary>Gives a UTC time as the value of an input of the type <c>datetime-local</c>, for example "2030-01-01T10:00:05".</summary>
    public static string InputTimeOf(DateTime utc) => utc.ToString(_inputTimeFormat, CultureInfo.InvariantCulture);

    /// <summary>Gives the target of a message, for example "User 1".</summary>
    public static string TargetOf(DeliveryTarget target) => $"{target.Kind} {target.Id}";

    /// <summary>Gives the tries of a message, for example "3 / 8".</summary>
    public static string TriesOf(DeliveryState delivery) =>
        string.Create(CultureInfo.InvariantCulture, $"{delivery.TryCount} / {delivery.MaxTryCount}");

    /// <summary>Gives the Bootstrap class of the badge of a status.</summary>
    public static string StatusClassOf(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Done => "text-bg-success",
        DeliveryStatus.Failed => "text-bg-danger",
        DeliveryStatus.Cancelled => "text-bg-secondary",
        _ => "text-bg-info",
    };

    /// <summary>Gives the result of a retry or a cancel, for example "Matched 5, changed 3.".</summary>
    public static string ResultOf(DeliveryActionResult result) =>
        string.Create(CultureInfo.InvariantCulture, $"Matched {result.Matched}, changed {result.Changed}.");

    /// <summary>Gives the result of a retry or a cancel of one message, with a reason when it changed nothing (D296).</summary>
    public static string OneMessageResultOf(DeliveryActionResult result) => result.Changed == 0 ? _noChange : ResultOf(result);

    /// <summary>
    /// Gives a JSON text with indents, written by <see cref="Utf8JsonWriter"/> with no reflection (D237), or the text itself
    /// when it is not JSON.
    /// </summary>
    public static string IndentedJsonOf(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, _indented))
            {
                document.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
