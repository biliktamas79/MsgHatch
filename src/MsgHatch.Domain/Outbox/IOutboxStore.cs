using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;

namespace MsgHatch.Domain.Outbox;

/// <summary>
/// The tenant store of the outbox (D127): a tenant service that adds, reads, counts, retries, and cancels the outgoing
/// messages of one tenant (D108).
/// A store returns an <see cref="Error"/> for a failure, and does not throw. A store never reads the clock.
/// A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Adds the outgoing messages of one event. A message whose id is already stored is ignored, so a retry of a
    /// partial batch is safe. A message with another tenant than the key of the store gives
    /// <see cref="Errors.ValidationFailed"/>, and the store adds nothing of the batch.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null for a success, else the error.</returns>
    ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct);

    /// <summary>
    /// Gives a page of the summaries of the outgoing messages of the tenant that match the query, newest first (D112). A
    /// summary has no content.
    /// </summary>
    /// <param name="query">The filters, the limit, and the cursor.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The page, or the error.</returns>
    ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct);

    /// <summary>Gives the whole outgoing message of the tenant, with its envelope, and all its publish tries in the order of their numbers. An unknown id, or the id of a message of another tenant, gives null.</summary>
    /// <param name="id">The id of the message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The message with its tries, null, or the error.</returns>
    ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Counts the outgoing messages of the tenant that match the query: one row for each message type, channel, status,
    /// and test flag (D164).
    /// </summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The rows, or the error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct);

    /// <summary>
    /// Retries the messages of the tenant with the given ids, each in one atomic operation (D111): a failed message gets
    /// the status pending, the next try time <paramref name="nowUtc"/>, and <c>TryCount</c> plus
    /// <paramref name="addedTries"/> as its <c>MaxTryCount</c>. A pending message with no active lease is due at
    /// <paramref name="nowUtc"/>. A done or cancelled message, a message with an active lease, and a message whose expiry
    /// is at or before <paramref name="nowUtc"/> do not change. An unknown id changes nothing.
    /// </summary>
    /// <param name="ids">The ids of the messages.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="addedTries">The number of new tries of a failed message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The number of changed messages, or the error.</returns>
    ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct);

    /// <summary>
    /// Cancels the pending messages with no active lease of the tenant with the given ids (D110). A cancelled message
    /// is never claimed again. The store records a publish try with the next try number, <paramref name="nowUtc"/> as its start and finish time, and the outcome <see cref="PublishOutcome.Cancelled"/>. It keeps the last error. An unknown id changes nothing.
    /// </summary>
    /// <param name="ids">The ids of the messages.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The number of changed messages, or the error.</returns>
    ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct);
}
