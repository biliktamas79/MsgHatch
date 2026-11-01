using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Outbox;

/// <summary>
/// The outbox dispatcher (D101). It runs one dispatch loop for each channel whose transport the host registered, and
/// only these loops send. A caller must not await one returned <see cref="ValueTask"/> two times.
/// </summary>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Runs the dispatch loop of each published channel until the token is cancelled, and then returns. It handles the
    /// error of each message inside the loop, and does not throw <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="ct">The token that stops the loops.</param>
    /// <returns>A task that completes when all loops stopped.</returns>
    ValueTask RunAsync(CancellationToken ct);

    /// <summary>Claims one batch for each published channel, and publishes each claimed message.</summary>
    /// <param name="batchSize">The maximum number of messages of each channel. A value less than 1 gives <see cref="Errors.ValidationFailed"/>.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The counts of all channels, or the error.</returns>
    ValueTask<ErrorOr<DispatchSummary>> DispatchPendingAsync(int batchSize, CancellationToken ct);
}
