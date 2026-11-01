using System.Diagnostics.CodeAnalysis;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Inbox;

/// <summary>
/// The dispatch store of the inbox (D127): one service for all tenants. It claims the due messages, and records the
/// result of each processing. A store returns an <see cref="Error"/> for a failure, and does not throw. A store never
/// reads the clock. A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface IInboxDispatchStore
{
    /// <summary>
    /// Claims the pending messages with <c>NextTryAtUtc</c> at or before <paramref name="nowUtc"/>, of all tenants, and
    /// sets their <c>NextTryAtUtc</c> and <c>LeaseUntilUtc</c> to <paramref name="nowUtc"/> plus <paramref name="lease"/>
    /// (D103). The claim is atomic between concurrent processors.
    /// </summary>
    /// <param name="maxCount">The maximum number of messages. A value less than 1 gives <see cref="Errors.ValidationFailed"/>.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="lease">The duration of the lease.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The claimed messages as their closed types, with their tenants, or the error.</returns>
    ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimDueAsync(int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct);

    /// <summary>Marks the stored message as processed: it adds 1 to the try count, sets the last try time, and ends the lease.</summary>
    /// <param name="message">The message.</param>
    /// <param name="triedAtUtc">The time of the processing.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null for a success, else the error.</returns>
    ValueTask<Error?> CompleteAsync(ReceivedMessage message, DateTime triedAtUtc, CancellationToken ct);

    /// <summary>
    /// Records a failed processing: it adds 1 to the try count, sets the last try time and the last error, and ends the
    /// lease. With a next try time, the message stays pending until that time. With no next try time, it fails permanently.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The text of the error.</param>
    /// <param name="triedAtUtc">The time of the processing.</param>
    /// <param name="nextTryAtUtc">The next try time, or null for a permanent failure.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null for a success, else the error.</returns>
    [SuppressMessage("Naming", "CA1716", Justification = "Section 4.6 of the design fixes the parameter name error.")]
    ValueTask<Error?> FailAsync(
        ReceivedMessage message, string error, DateTime triedAtUtc, DateTime? nextTryAtUtc, CancellationToken ct);

    /// <summary>Deletes each stored message with <see cref="MessageFlags.Test"/> of all tenants. It deletes no message without the flag.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The number of deleted messages, or the error.</returns>
    ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct);
}
