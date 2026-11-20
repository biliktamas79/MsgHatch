using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The queries of the delivery management for a UI of the traffic and the history (section 4.19, D107, D289). It is a tenant
/// service: its message members read the tenant stores of its key, and <see cref="GetChannelsAsync"/> gives the channels of
/// all tenants, because a channel is global (D106). A member returns an <see cref="Error"/> for a failure, and does not
/// throw: <see cref="Errors.ValidationFailed"/> for a query that breaks <see cref="DeliveryRules"/>, and
/// <see cref="Errors.NotFound"/> for an unknown id. A caller must not await one returned <see cref="ValueTask{TResult}"/>
/// two times.
/// </summary>
public interface IDeliveryQueries
{
    /// <summary>Gives the state of each registered channel in all tenants: its down state, and its pending real and test messages (D164).</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>One state for each registered channel, or the error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<ChannelStatus>>> GetChannelsAsync(CancellationToken ct);

    /// <summary>Counts the received and the outgoing messages of the tenant (D164). With no inbox store, the inbox counts are empty.</summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The counts, or the error.</returns>
    ValueTask<ErrorOr<TrafficReport>> GetTrafficAsync(TrafficQuery query, CancellationToken ct);

    /// <summary>Gives a page of the received messages of the tenant, newest first. With no inbox store, the page is empty.</summary>
    /// <param name="query">The filters, the limit, and the cursor.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The page, or the error.</returns>
    ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> GetReceivedMessagesAsync(InboxQuery query, CancellationToken ct);

    /// <summary>Gives a page of the outgoing messages of the tenant, newest first.</summary>
    /// <param name="query">The filters, the limit, and the cursor.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The page, or the error.</returns>
    ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> GetOutgoingMessagesAsync(OutboxQuery query, CancellationToken ct);

    /// <summary>
    /// Gives the summary, the route trace as JSON text, and all publish tries of an outgoing message of the tenant, with no
    /// envelope (D290).
    /// </summary>
    /// <param name="id">The id of the message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The detail, or the error.</returns>
    ValueTask<ErrorOr<OutgoingMessageDetail>> GetOutgoingMessageAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Gives a whole received message of the tenant with its event as the JSON text of <c>MsgHatchJson</c> (D109, D290). With
    /// no inbox store, each id gives <see cref="Errors.NotFound"/>.
    /// </summary>
    /// <param name="id">The id of the message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The content, or the error.</returns>
    ValueTask<ErrorOr<MessageContent>> GetReceivedContentAsync(Guid id, CancellationToken ct);

    /// <summary>Gives a whole outgoing message of the tenant with its envelope as the JSON text of <c>MsgHatchJson</c> (D109, D290).</summary>
    /// <param name="id">The id of the message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The content, or the error.</returns>
    ValueTask<ErrorOr<MessageContent>> GetOutgoingContentAsync(Guid id, CancellationToken ct);
}
