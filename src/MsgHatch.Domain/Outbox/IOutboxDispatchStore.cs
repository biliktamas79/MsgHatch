using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Outbox;

/// <summary>
/// The dispatch store of the outbox (D127): one service for all tenants. A store returns an <see cref="Error"/>
/// for a failure, and does not throw. A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface IOutboxDispatchStore
{
    /// <summary>
    /// Deletes each stored message with <see cref="MessageFlags.Test"/> of all tenants, with its publish tries.
    /// It deletes no message without the flag.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The number of deleted messages, or the error.</returns>
    ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct);
}
