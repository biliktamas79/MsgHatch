using System.Text.Json;
using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Api.Client;

/// <summary>
/// The delivery queries of one tenant key over HTTP (D227, D289, D294). A query runs <see cref="DeliveryRules"/> first, and
/// gives the error of a rule with no HTTP call (D292). Each request sends the tenant of the key as <c>tenantId</c>. The
/// content is the text of the JSON body, as the server writes it with its content type registry (D290). A content body that
/// is not a JSON object, for example the JSON null, gives <see cref="Errors.SerializationFailed"/>.
/// </summary>
/// <param name="api">The generated client of the routes.</param>
/// <param name="key">The tenant key, whose tenant each request sends as <c>tenantId</c>.</param>
internal sealed class HttpDeliveryQueries(DeliveryApi api, TenantKey key) : IDeliveryQueries
{
    private string? Tenant => key.Tenant?.Value;

    public async ValueTask<ErrorOr<IReadOnlyList<ChannelStatus>>> GetChannelsAsync(CancellationToken ct) =>
        ApiResults.All(await api.GetChannelsAsync(Tenant, ct), static channel => channel.ToStatus());

    public async ValueTask<ErrorOr<TrafficReport>> GetTrafficAsync(TrafficQuery query, CancellationToken ct) =>
        DeliveryRules.Check(query) is { } invalid
            ? invalid
            : ApiResults.One(
                await api.GetTrafficAsync(query.CreatedFromUtc, query.CreatedToUtc, query.Status, query.IncludeTest, Tenant, ct),
                static report => report.ToReport());

    public async ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> GetReceivedMessagesAsync(InboxQuery query, CancellationToken ct) =>
        DeliveryRules.Check(query) is { } invalid
            ? invalid
            : ApiResults.One(
                await api.GetReceivedMessagesAsync(
                    query.Ids, query.Status, query.Type?.Name, query.CreatedFromUtc, query.CreatedToUtc, query.DueBeforeUtc, query.IncludeTest,
                    query.Limit, CursorTextOf(query.After), Tenant, ct),
                static page => PageOf(page, static summary => summary.ToSummary()));

    public async ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> GetOutgoingMessagesAsync(OutboxQuery query, CancellationToken ct) =>
        DeliveryRules.Check(query) is { } invalid
            ? invalid
            : ApiResults.One(
                await api.GetOutgoingMessagesAsync(
                    query.Ids, query.Status, query.Type?.Name, query.Channel?.Name, query.Target?.Kind, query.Target?.Id, query.SourceMessageId,
                    query.CreatedFromUtc, query.CreatedToUtc, query.DueBeforeUtc, query.IncludeTest, query.Limit, CursorTextOf(query.After), Tenant, ct),
                static page => PageOf(page, static summary => summary.ToSummary()));

    public async ValueTask<ErrorOr<OutgoingMessageDetail>> GetOutgoingMessageAsync(Guid id, CancellationToken ct) =>
        ApiResults.One(await api.GetOutgoingMessageAsync(id, Tenant, ct), static detail => detail.ToDetail());

    public async ValueTask<ErrorOr<MessageContent>> GetReceivedContentAsync(Guid id, CancellationToken ct) =>
        ContentOf(id, await api.GetReceivedContentAsync(id, Tenant, ct));

    public async ValueTask<ErrorOr<MessageContent>> GetOutgoingContentAsync(Guid id, CancellationToken ct) =>
        ContentOf(id, await api.GetOutgoingContentAsync(id, Tenant, ct));

    private static string? CursorTextOf(MessageCursor? cursor) => cursor is { } after ? DeliveryApiRoutes.CursorText(after) : null;

    private static ErrorOr<MessageContent> ContentOf(Guid id, ErrorOr<JsonElement> body) =>
        body.IsError ? body.Error
        : body.Value.ValueKind != JsonValueKind.Object ? new Error(Errors.SerializationFailed, "The content of a message is a JSON object.")
        : new MessageContent(id, body.Value.GetRawText());

    /// <summary>Maps a page body. No list, a null item, an item that is not valid, or a cursor text that does not parse gives <see cref="Errors.SerializationFailed"/>.</summary>
    private static ErrorOr<MessagePage<T>> PageOf<TBody, T>(MessagePageResponse<TBody> page, Func<TBody, ErrorOr<T>> map)
        where TBody : class
    {
        var items = page.Items is null
            ? new Error(Errors.SerializationFailed, "A page has a list of items.")
            : ApiResults.All(ErrorOr<IReadOnlyList<TBody>>.ForValue(page.Items), map);
        if (items.IsError)
        {
            return items.Error;
        }

        if (page.Next is null)
        {
            return new MessagePage<T>(items.Value, Next: null);
        }

        return DeliveryApiRoutes.TryParseCursor(page.Next, out var next)
            ? new MessagePage<T>(items.Value, next)
            : new Error(Errors.SerializationFailed, "The cursor of the next page is not {CreatedAtUtc}_{Id}.");
    }
}
