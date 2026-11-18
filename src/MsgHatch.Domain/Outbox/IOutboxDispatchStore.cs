using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Domain.Outbox;

/// <summary>
/// The dispatch store of the outbox (D127): one service for all tenants. It claims the due messages, records the
/// tries, and keeps the down state of each channel (D106). A store returns an <see cref="Error"/> for a failure, and
/// does not throw. A store never reads the clock. A caller must not await one returned
/// <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface IOutboxDispatchStore
{
    /// <summary>
    /// Claims the pending messages of one channel with <c>NextTryAtUtc</c> at or before <paramref name="nowUtc"/>, of all
    /// tenants, and sets their <c>NextTryAtUtc</c> and <c>LeaseUntilUtc</c> to <paramref name="nowUtc"/> plus
    /// <paramref name="lease"/> (D103, D104). The claim is atomic between concurrent dispatchers. A channel with a down
    /// state claims nothing, and the result has that state.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="maxCount">The maximum number of messages. A value less than 1 gives <see cref="Errors.ValidationFailed"/>.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="lease">The duration of the lease.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The claimed messages as their closed types, with their tenants, or the error.</returns>
    ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimDueAsync(
        ChannelId channel, int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct);

    /// <summary>
    /// Adds the publish try to the stored message, and sets its delivery state in one atomic operation: the try count,
    /// the last try time, the last error, the status, and the end of the lease. It changes nothing when the stored
    /// <c>TryCount</c> is not the try number minus 1, so a repeated call never records a try two times.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="publishTry">The try.</param>
    /// <param name="nextTryAtUtc">The next try time of a transient error, or null.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null for a success, else the error.</returns>
    ValueTask<Error?> RecordTryAsync(OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct);

    /// <summary>
    /// Deletes each stored message with <see cref="MessageFlags.Test"/> of all tenants, with its publish tries.
    /// It deletes no message without the flag.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The number of deleted messages, or the error.</returns>
    ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct);

    /// <summary>
    /// Counts the messages of all tenants that match the query, for the state of the channels (section 4.19): one row
    /// for each message type, channel, status, and test flag (D164).
    /// </summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The rows, or the error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct);

    /// <summary>
    /// Deletes up to <paramref name="maxCount"/> finished messages of all tenants (D128), with their publish tries. A finished message has the
    /// status <see cref="DeliveryStatus.Done"/>, <see cref="DeliveryStatus.Failed"/>, or <see cref="DeliveryStatus.Cancelled"/>,
    /// and a finish time before <paramref name="finishedBeforeUtc"/>. The finish time is <c>LastTryAtUtc</c>, or
    /// <c>CreatedAtUtc</c> for a message with no try. It never deletes a pending message. A purged id is a new message
    /// when it comes again.
    /// </summary>
    /// <param name="finishedBeforeUtc">The time before which a message must finish.</param>
    /// <param name="maxCount">The maximum number of messages. A value less than 1 gives <see cref="Errors.ValidationFailed"/>.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The number of deleted messages, or the error.</returns>
    ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct);

    /// <summary>Gives the down state of each channel whose latest state row is down (D106, D277).</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The down states, or the error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct);

    /// <summary>
    /// Marks a channel down: it adds the row of <see cref="ChannelState.AfterDown"/> when the state changes (D277). With the
    /// reason <see cref="ChannelDownReason.Errors"/>, it adds a row only on an up channel, so the first loop that finds the
    /// errors wins. With the reason <see cref="ChannelDownReason.Operator"/>, it adds a row unless the channel is paused. A
    /// store keeps a unique key on the channel and the version, so of two parallel changes only the first adds its row, and
    /// the other tries again with the new latest row (<see cref="ChannelState.Change"/>).
    /// </summary>
    /// <param name="down">The down state.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True when it added a row, false for no change, or the error.</returns>
    ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct);

    /// <summary>
    /// Marks a channel up: it adds the row of <see cref="ChannelState.AfterUp"/> when the channel is down (D277). With a
    /// reason, it changes only a down state with that reason, so a health check never ends a pause of a person.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="reason">The reason to end, or null for each reason.</param>
    /// <param name="upAtUtc">The up time, the start of the new row.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True when it added a row, false for no change, or the error.</returns>
    ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, DateTime upAtUtc, CancellationToken ct);

    /// <summary>
    /// Gives the state rows of all channels with <see cref="ChannelState.SinceUtc"/> at or after <paramref name="fromUtc"/>,
    /// and the row of each channel that was current at <paramref name="fromUtc"/>, newest first: by
    /// <see cref="ChannelState.SinceUtc"/>, then by the ordinal order of the channel names, then by the version (D277). A
    /// store keeps all rows.
    /// </summary>
    /// <param name="fromUtc">The earliest start of a row, besides the row that was current at that time.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The rows, or the error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelStatesAsync(DateTime fromUtc, CancellationToken ct);

    /// <summary>
    /// Moves the next check time of the latest state row to <paramref name="nextCheckAtUtc"/>, only when the channel is down
    /// with the reason <see cref="ChannelDownReason.Errors"/> and its next check time is at or before
    /// <paramref name="nowUtc"/>. It is the only update of a state row (D277). It is atomic between concurrent publisher
    /// instances, so one instance at a time runs the health check.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="nextCheckAtUtc">The time of the check after this one.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True when this caller runs the health check, or the error.</returns>
    ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct);
}
