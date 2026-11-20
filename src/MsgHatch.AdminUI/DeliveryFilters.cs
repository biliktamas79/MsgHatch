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

    /// <summary>The earliest creation time, marked as UTC, or null.</summary>
    public DateTime? CreatedFromUtc => Utc(CreatedFrom);

    /// <summary>The creation time that ends the range, marked as UTC, or null.</summary>
    public DateTime? CreatedToUtc => Utc(CreatedTo);

    /// <summary>Sets the filters of both lists from the values of the address. A time that is not valid is no filter.</summary>
    public void SetFromAddress(string? status, string? type, string? from, string? to, bool? test)
    {
        (Status, Type) = (status ?? string.Empty, type ?? string.Empty);
        (CreatedFrom, CreatedTo, IncludeTest) = (TimeOf(from), TimeOf(to), test == true);
    }

    /// <summary>Gives the query of the outbox with a limit and a cursor.</summary>
    public ErrorOr<OutboxQuery> ToOutboxQuery(int limit, MessageCursor? after)
    {
        var status = Optional(Status, StatusOf);
        var type = Optional(Type, TypeOf);
        var channel = Optional(Channel, static text => NameOf(text, "channel", static valid => new ChannelId(valid)));
        var target = TargetOf();
        var source = Optional<Guid>(Source, static text => Guid.TryParse(text, out var id) ? id : Invalid("The source message is not an id."));
        return (status.Error ?? type.Error ?? channel.Error ?? target.Error ?? source.Error) is { } invalid
            ? invalid
            : new OutboxQuery(
                Ids: null, status.Value, type.Value, channel.Value, target.Value, source.Value, CreatedFromUtc, CreatedToUtc,
                DueBeforeUtc: null, IncludeTest, limit, after);
    }

    /// <summary>Gives the query of the inbox with a limit and a cursor, from the status, the message type, the times, and the test flag.</summary>
    public ErrorOr<InboxQuery> ToInboxQuery(int limit, MessageCursor? after)
    {
        var status = Optional(Status, StatusOf);
        var type = Optional(Type, TypeOf);
        return (status.Error ?? type.Error) is { } invalid
            ? invalid
            : new InboxQuery(Ids: null, status.Value, type.Value, CreatedFromUtc, CreatedToUtc, DueBeforeUtc: null, IncludeTest, limit, after);
    }

    /// <summary>Gives the query of the traffic from the status, the times, and the test flag.</summary>
    public ErrorOr<TrafficQuery> ToTrafficQuery()
    {
        var status = Optional(Status, StatusOf);
        return status.Error is { } invalid ? invalid : new TrafficQuery(CreatedFromUtc, CreatedToUtc, status.Value, IncludeTest);
    }

    private static DateTime? TimeOf(string? text) =>
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

    private static ErrorOr<MessageType> TypeOf(string text) => NameOf(text, "message type", static valid => new MessageType(valid));

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
