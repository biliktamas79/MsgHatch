using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AspNetCore;

/// <summary>
/// Reads the queries and the bodies of the delivery endpoints into the queries of <see cref="IDeliveryQueries"/> and
/// <see cref="IDeliveryCommands"/> (section 4.19). Each value from outside is checked: an invalid value gives
/// <see cref="Errors.ValidationFailed"/>, and so 400. A cursor is the text <c>{CreatedAtUtc}_{Id}</c>, with the time in the
/// format of D57.
/// </summary>
internal static class DeliveryRequestValues
{
    private const DateTimeStyles _utc = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

    public static ErrorOr<MessageCursor> CursorOf(string text) =>
        DeliveryApiRoutes.TryParseCursor(text, out var cursor) ? cursor : Invalid("The cursor is not {CreatedAtUtc}_{Id}.");

    public static ErrorOr<TenantId?> TenantOf(StringValues values) => Optional(values, static text => TenantId.Create(text));

    public static ErrorOr<ChannelId> ChannelOf(string name) => NameOf(name, static valid => new ChannelId(valid));

    public static ErrorOr<TrafficQuery> TrafficQueryOf(IQueryCollection query)
    {
        var from = Optional(query[DeliveryApiRoutes.Query.CreatedFrom], TimeOf);
        var to = Optional(query[DeliveryApiRoutes.Query.CreatedTo], TimeOf);
        var status = Optional(query[DeliveryApiRoutes.Query.Status], EnumOf<DeliveryStatus>);
        var includeTest = Optional(query[DeliveryApiRoutes.Query.IncludeTest], BoolOf);
        return from.Error ?? to.Error ?? status.Error ?? includeTest.Error
            ?? (ErrorOr<TrafficQuery>)new TrafficQuery(from.Value, to.Value, status.Value, includeTest.Value ?? false);
    }

    public static ErrorOr<InboxQuery> InboxQueryOf(IQueryCollection query)
    {
        var shared = SharedOf(query);
        return shared.IsError ? shared.Error! : shared.Value.ToInbox();
    }

    public static ErrorOr<OutboxQuery> OutboxQueryOf(IQueryCollection query)
    {
        var shared = SharedOf(query);
        var channel = Optional(query[DeliveryApiRoutes.Query.Channel], ChannelOf);
        var target = TargetOf(First(query[DeliveryApiRoutes.Query.TargetKind]), First(query[DeliveryApiRoutes.Query.TargetId]));
        var source = Optional(query[DeliveryApiRoutes.Query.SourceMessageId], GuidOf);
        return shared.Error ?? channel.Error ?? target.Error ?? source.Error
            ?? (ErrorOr<OutboxQuery>)shared.Value.ToOutbox(channel.Value, target.Value, source.Value);
    }

    public static ErrorOr<InboxQuery> InboxQueryOf(InboxFilterRequest body)
    {
        var type = Optional(body.MessageType, MessageTypeOf);
        return type.Error ?? (ErrorOr<InboxQuery>)new InboxQuery(
            body.Ids, body.Status, type.Value, Utc(body.CreatedFrom), Utc(body.CreatedTo), Utc(body.DueBefore), body.IncludeTest);
    }

    public static ErrorOr<OutboxQuery> OutboxQueryOf(OutboxFilterRequest body)
    {
        var type = Optional(body.MessageType, MessageTypeOf);
        var channel = Optional(body.Channel, ChannelOf);
        var target = TargetOf(body.TargetKind?.ToString(), body.TargetId);
        return type.Error ?? channel.Error ?? target.Error ?? (ErrorOr<OutboxQuery>)new OutboxQuery(
            body.Ids, body.Status, type.Value, channel.Value, target.Value, body.SourceMessageId, Utc(body.CreatedFrom),
            Utc(body.CreatedTo), Utc(body.DueBefore), body.IncludeTest);
    }

    /// <summary>A time of a body in UTC. A time with no zone is UTC.</summary>
    public static DateTime Utc(DateTime time) =>
        time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time.ToUniversalTime();

    private static DateTime? Utc(DateTime? time) => time is { } value ? Utc(value) : null;

    private static ErrorOr<SharedFilters> SharedOf(IQueryCollection query)
    {
        var ids = IdsOf(query[DeliveryApiRoutes.Query.Ids]);
        var status = Optional(query[DeliveryApiRoutes.Query.Status], EnumOf<DeliveryStatus>);
        var type = Optional(query[DeliveryApiRoutes.Query.MessageType], MessageTypeOf);
        var from = Optional(query[DeliveryApiRoutes.Query.CreatedFrom], TimeOf);
        var to = Optional(query[DeliveryApiRoutes.Query.CreatedTo], TimeOf);
        var dueBefore = Optional(query[DeliveryApiRoutes.Query.DueBefore], TimeOf);
        var includeTest = Optional(query[DeliveryApiRoutes.Query.IncludeTest], BoolOf);
        var limit = Optional(query[DeliveryApiRoutes.Query.Limit], IntOf);
        var after = Optional(query[DeliveryApiRoutes.Query.After], CursorOf);
        return ids.Error ?? status.Error ?? type.Error ?? from.Error ?? to.Error ?? dueBefore.Error ?? includeTest.Error
            ?? limit.Error ?? after.Error
            ?? (ErrorOr<SharedFilters>)new SharedFilters(
                ids.Value, status.Value, type.Value, from.Value, to.Value, dueBefore.Value, includeTest.Value ?? false,
                limit.Value ?? 50, after.Value);
    }

    private static ErrorOr<IReadOnlyList<Guid>?> IdsOf(StringValues values)
    {
        if (values.Count == 0)
        {
            return ErrorOr<IReadOnlyList<Guid>?>.ForValue(null);
        }

        var ids = new Guid[values.Count];
        for (var index = 0; index < ids.Length; index++)
        {
            if (!Guid.TryParse(values[index], out ids[index]))
            {
                return Invalid("An id is not a GUID.");
            }
        }

        return ErrorOr<IReadOnlyList<Guid>?>.ForValue(ids);
    }

    private static ErrorOr<DeliveryTarget?> TargetOf(string? kind, string? id) =>
        (kind, id) switch
        {
            (null, null) => (DeliveryTarget?)null,
            ({ } kindText, _) => EnumOf<DeliveryTargetKind>(kindText).ThenIfSuccess(valid => TargetOf(valid, id)),
            _ => Invalid("A target needs both targetKind and targetId."),
        };

    private static ErrorOr<DeliveryTarget?> TargetOf(DeliveryTargetKind kind, string? id) =>
        (kind, id) switch
        {
            (DeliveryTargetKind.Tenant, null or "") => new DeliveryTarget(kind, ""),
            (not DeliveryTargetKind.Tenant, { Length: > 0 }) => new DeliveryTarget(kind, id),
            _ => Invalid("A tenant target has no targetId, and each other target needs one."),
        };

    private static ErrorOr<T?> Optional<T>(StringValues values, Func<string, ErrorOr<T>> parse)
        where T : struct =>
        Optional(First(values), parse);

    private static ErrorOr<T?> Optional<T>(string? value, Func<string, ErrorOr<T>> parse)
        where T : struct
    {
        if (value is null)
        {
            return (T?)null;
        }

        var parsed = parse(value);
        return parsed.IsError ? parsed.Error! : parsed.Value;
    }

    private static string? First(StringValues values) => values.Count == 0 ? null : values[0];

    public static ErrorOr<MessageType> MessageTypeOf(string name) => NameOf(name, static valid => new MessageType(valid));

    private static ErrorOr<T> EnumOf<T>(string text)
        where T : struct, Enum =>
        Enum.GetNames<T>().Contains(text, StringComparer.Ordinal) ? Enum.Parse<T>(text) : Invalid($"The value {text} is not a {typeof(T).Name}.");

    private static ErrorOr<DateTime> TimeOf(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, _utc, out var time) ? time : Invalid("A time is not ISO 8601.");

    private static ErrorOr<bool> BoolOf(string text) => bool.TryParse(text, out var value) ? value : Invalid("A flag is not true or false.");

    private static ErrorOr<int> IntOf(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : Invalid("A number is not an integer.");

    private static ErrorOr<Guid> GuidOf(string text) => Guid.TryParse(text, out var id) ? id : Invalid("An id is not a GUID.");

    private static ErrorOr<T> NameOf<T>(string name, Func<string, T> create)
    {
        try
        {
            return create(name);
        }
        catch (ArgumentException exception)
        {
            return new Error(Errors.ValidationFailed, exception.Message);
        }
    }

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);

    private sealed record SharedFilters(
        IReadOnlyList<Guid>? Ids, DeliveryStatus? Status, MessageType? Type, DateTime? CreatedFromUtc, DateTime? CreatedToUtc,
        DateTime? DueBeforeUtc, bool IncludeTest, int Limit, MessageCursor? After)
    {
        public InboxQuery ToInbox() => new(Ids, Status, Type, CreatedFromUtc, CreatedToUtc, DueBeforeUtc, IncludeTest, Limit, After);

        public OutboxQuery ToOutbox(ChannelId? channel, DeliveryTarget? target, Guid? sourceMessageId) =>
            new(Ids, Status, Type, channel, target, sourceMessageId, CreatedFromUtc, CreatedToUtc, DueBeforeUtc, IncludeTest, Limit, After);
    }
}
