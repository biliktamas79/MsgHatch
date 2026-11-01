using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Inbox;

/// <summary>
/// The inbox processor. It runs one dispatch loop for the inbox, and processes each claimed message in its own DI
/// scope (D130). A caller must not await one returned <see cref="ValueTask"/> two times.
/// </summary>
public interface IInboxProcessor
{
    /// <summary>
    /// Runs the dispatch loop of the inbox until the token is cancelled, and then returns. It handles the error of each
    /// message inside the loop, and does not throw <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="ct">The token that stops the loop.</param>
    /// <returns>A task that completes when the loop stopped.</returns>
    ValueTask RunAsync(CancellationToken ct);

    /// <summary>Claims one batch of the inbox, and processes each claimed message.</summary>
    /// <param name="batchSize">The maximum number of messages. A value less than 1 gives <see cref="Errors.ValidationFailed"/>.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The counts, or the error.</returns>
    ValueTask<ErrorOr<DispatchSummary>> ProcessPendingAsync(int batchSize, CancellationToken ct);
}
