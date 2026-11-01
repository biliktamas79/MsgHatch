using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Outbox;

/// <summary>
/// The tenant store of the outbox (D127): a tenant service that adds the outgoing messages of one tenant.
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
}
