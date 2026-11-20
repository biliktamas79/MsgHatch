using System.Globalization;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AdminUI;

/// <summary>
/// The values of the filter fields of a delivery list, as the text of their inputs (D295). The page binds each field, and
/// gives the query of the server from them. An empty field is no filter. A value that is not valid gives
/// <see cref="Errors.ValidationFailed"/>, so the list shows the error in place of its rows.
/// </summary>
internal sealed class DeliveryFilters
{
    private const DateTimeStyles _utc = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    public string Status { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;

    public string TargetKind { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    /// <summary>The earliest creation time in UTC, from an input of the type <c>datetime-local</c>, which has no time zone.</summary>
    public DateTime? CreatedFrom { get; set; }

    /// <summary>The creation time in UTC that ends the range, from an input of the type <c>datetime-local</c>.</summary>
    public DateTime? CreatedTo { get; set; }

    public bool IncludeTest { get; set; }

    /// <summary>Gives the query of the outbox with a limit and a cursor.</summary>
    public ErrorOr<OutboxQuery> ToOutboxQuery(int limit, MessageCursor? after)
    {
        var status = Optional(Status, StatusOf);
        var type = Optional(Type, static text => NameOf(text, "message type", static valid => new MessageType(valid)));
        var channel = Optional(Channel, static text => NameOf(text, "channel", static valid => new ChannelId(valid)));
        var target = TargetOf();
        var source = Optional<Guid>(Source, static text => Guid.TryParse(text, out var id) ? id : Invalid("The source message is not an id."));
        return (status.Error ?? type.Error ?? channel.Error ?? target.Error ?? source.Error) is { } invalid
            ? invalid
            : new OutboxQuery(
                Ids: null, status.Value, type.Value, channel.Value, target.Value, source.Value, Utc(CreatedFrom), Utc(CreatedTo),
                DueBeforeUtc: null, IncludeTest, limit, after);
    }

    /// <summary>Gives the time of a value of the address in UTC, or null for no value or a value that is not a time.</summary>
    public static DateTime? TimeOf(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, _utc, out var time) ? time : null;

    private ErrorOr<DeliveryTarget?> TargetOf()
    {
        var (kind, id) = (TargetKind.Trim(), TargetId.Trim());
        if (kind.Length == 0 && id.Length == 0)
        {
            return ErrorOr<DeliveryTarget?>.ForValue(null);
        }

        return kind.Length == 0 || id.Length == 0 ? Invalid("A target needs a kind and an id.")
            : Enum.TryParse<DeliveryTargetKind>(kind, out var valid) && Enum.IsDefined(valid) ? new DeliveryTarget(valid, id)
            : Invalid($"The target kind {kind} is not known.");
    }

    private static ErrorOr<DeliveryStatus> StatusOf(string text) =>
        Enum.TryParse<DeliveryStatus>(text, out var status) && Enum.IsDefined(status) ? status : Invalid($"The status {text} is not known.");

    private static DateTime? Utc(DateTime? time) => time is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null;

    private static ErrorOr<T?> Optional<T>(string text, Func<string, ErrorOr<T>> parse)
        where T : struct
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ErrorOr<T?>.ForValue(null);
        }

        var parsed = parse(text.Trim());
        return parsed.IsError ? parsed.Error : parsed.Value;
    }

    private static ErrorOr<T> NameOf<T>(string text, string kind, Func<string, T> create)
    {
        try
        {
            return create(text);
        }
        catch (ArgumentException)
        {
            return Invalid($"The {kind} {text} is not a valid name.");
        }
    }

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);
}
