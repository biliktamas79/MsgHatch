using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

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

    /// <summary>Gives the down state of each down channel (D106).</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The down states, or the error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct);

    /// <summary>
    /// Marks a channel down. With the reason <see cref="ChannelDownReason.Errors"/>, it writes only when the channel
    /// has no down state, so the first loop that finds the errors wins. With the reason
    /// <see cref="ChannelDownReason.Operator"/>, it always writes.
    /// </summary>
    /// <param name="down">The down state.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True when it wrote the state, or the error.</returns>
    ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct);

    /// <summary>
    /// Removes the down state of a channel. With a reason, it removes only a down state with that reason, so a health
    /// check never ends a pause of a person.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="reason">The reason to end, or null for each reason.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True when it removed a down state, or the error.</returns>
    ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, CancellationToken ct);

    /// <summary>
    /// Moves the next check time to <paramref name="nextCheckAtUtc"/>, only when the channel is down with the reason
    /// <see cref="ChannelDownReason.Errors"/> and its next check time is at or before <paramref name="nowUtc"/>. It is
    /// atomic between concurrent publisher instances, so one instance at a time runs the health check.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="nextCheckAtUtc">The time of the check after this one.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True when this caller runs the health check, or the error.</returns>
    ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct);
}
