using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The API for a UI of the traffic, the history, and the repairs (section 4.19, D107). It is a tenant service: its
/// message members use the tenant stores of its key. Its channel members, <see cref="DeleteTestDataAsync"/>, and
/// <see cref="PurgeAsync"/> act on all tenants, because a channel is global (D106). So only the manager of
/// <see cref="TenantKey.None"/> pauses and resumes a channel, deletes the test data, and purges. A member returns an
/// <see cref="Error"/> for a failure, and does not throw: <see cref="Errors.ValidationFailed"/> for an invalid
/// request, with no store call, and <see cref="Errors.NotFound"/> for an unknown id or channel. A caller must not
/// await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface IDeliveryManager
{
    /// <summary>Gives the state of each registered channel in all tenants: its down state, and its pending real and test messages (D164).</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>One state for each registered channel, or the error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<ChannelStatus>>> GetChannelsAsync(CancellationToken ct);

    /// <summary>
    /// Pauses a channel for each publisher instance (D106): each publisher instance of the channel stops at its next
    /// claim, until a resume. Only the manager of <see cref="TenantKey.None"/> can pause.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="note">The text of the person, at most 500 characters, or null.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null for a success, else the error.</returns>
    ValueTask<Error?> PauseChannelAsync(ChannelId channel, string? note, CancellationToken ct);

    /// <summary>
    /// Ends the down state of a channel for each reason, and notifies the topic of the channel, so that the publishers
    /// claim at once. Only the manager of <see cref="TenantKey.None"/> can resume.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null for a success, else the error.</returns>
    ValueTask<Error?> ResumeChannelAsync(ChannelId channel, CancellationToken ct);

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

    /// <summary>Gives the summary, the route trace, and all publish tries of an outgoing message of the tenant, with no envelope.</summary>
    /// <param name="id">The id of the message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The detail, or the error.</returns>
    ValueTask<ErrorOr<OutgoingMessageDetail>> GetOutgoingMessageAsync(Guid id, CancellationToken ct);

    /// <summary>Gives a whole received message of the tenant, with its event (D109).</summary>
    /// <param name="id">The id of the message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The message, or the error.</returns>
    ValueTask<ErrorOr<ReceivedMessage>> GetReceivedContentAsync(Guid id, CancellationToken ct);

    /// <summary>Gives a whole outgoing message of the tenant, with its envelope (D109).</summary>
    /// <param name="id">The id of the message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The message, or the error.</returns>
    ValueTask<ErrorOr<OutgoingMessage>> GetOutgoingContentAsync(Guid id, CancellationToken ct);

    /// <summary>Retries the received messages of the tenant that match the query (D111). The query needs at least one filter.</summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The numbers of the matched and the changed messages, or the error.</returns>
    ValueTask<ErrorOr<DeliveryActionResult>> RetryReceivedAsync(InboxQuery query, CancellationToken ct);

    /// <summary>Cancels the received messages of the tenant that match the query (D110). The query needs at least one filter.</summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The numbers of the matched and the changed messages, or the error.</returns>
    ValueTask<ErrorOr<DeliveryActionResult>> CancelReceivedAsync(InboxQuery query, CancellationToken ct);

    /// <summary>Retries the outgoing messages of the tenant that match the query (D111). The query needs at least one filter.</summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The numbers of the matched and the changed messages, or the error.</returns>
    ValueTask<ErrorOr<DeliveryActionResult>> RetryOutgoingAsync(OutboxQuery query, CancellationToken ct);

    /// <summary>Cancels the outgoing messages of the tenant that match the query (D110). The query needs at least one filter.</summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The numbers of the matched and the changed messages, or the error.</returns>
    ValueTask<ErrorOr<DeliveryActionResult>> CancelOutgoingAsync(OutboxQuery query, CancellationToken ct);

    /// <summary>
    /// Deletes each message with <see cref="MessageFlags.Test"/> of all tenants from the inbox and the outbox (section 4.18).
    /// Only the manager of <see cref="TenantKey.None"/> can delete the test data.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The numbers of the deleted messages, or the error.</returns>
    ValueTask<ErrorOr<DeletedMessages>> DeleteTestDataAsync(CancellationToken ct);

    /// <summary>
    /// Deletes up to <paramref name="maxCount"/> finished messages of all tenants from the inbox, and up to
    /// <paramref name="maxCount"/> from the outbox (D128). Only the manager of <see cref="TenantKey.None"/> can purge.
    /// </summary>
    /// <param name="finishedBeforeUtc">The time before which a message must finish. It is not after the current time.</param>
    /// <param name="maxCount">The maximum number of messages of each store, at least 1.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The numbers of the deleted messages, or the error.</returns>
    ValueTask<ErrorOr<DeletedMessages>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct);
}
