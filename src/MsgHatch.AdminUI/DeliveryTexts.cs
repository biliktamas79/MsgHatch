using System.Globalization;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AdminUI;

/// <summary>The texts of the delivery pages (D295 to D298).</summary>
internal static class DeliveryTexts
{
    /// <summary>The format of a time in an input of the type <c>datetime-local</c> and in an address, in UTC to the second.</summary>
    private const string _inputTimeFormat = "yyyy-MM-dd'T'HH:mm:ss";

    /// <summary>Gives a UTC time to the second, for example "2030-01-01 10:00:05 UTC".</summary>
    public static string TimeOf(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

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
}
