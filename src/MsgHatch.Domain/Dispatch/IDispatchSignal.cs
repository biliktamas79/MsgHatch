using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// Wakes a waiting dispatch loop when new messages wait for its topic (D102). A signal is a hint: a lost signal only
/// delays a claim until the next due time or the next poll, and many notifications before one wait cause one wake-up.
/// A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface IDispatchSignal
{
    /// <summary>Notifies the loops of a topic. It returns at once, and never waits for a reader.</summary>
    /// <param name="topic">The topic.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null for a success, else the error.</returns>
    ValueTask<Error?> NotifyAsync(DispatchTopic topic, CancellationToken ct);

    /// <summary>
    /// Waits for a notification of a topic. A notification after the last wait for the topic, also before the first
    /// wait, completes the wait at once.
    /// </summary>
    /// <param name="topic">The topic.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// Null for a notification, <see cref="Errors.OperationCancelled"/> when the token is cancelled, or another error
    /// when the signal does not work.
    /// </returns>
    ValueTask<Error?> WaitAsync(DispatchTopic topic, CancellationToken ct);
}
