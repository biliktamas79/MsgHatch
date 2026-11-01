using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Inbox;

/// <summary>
/// The tenant store of the inbox (D127): a tenant service that adds the received messages of one tenant.
/// A store returns an <see cref="Error"/> for a failure, and does not throw. A store never reads the clock.
/// A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Adds a received message. The key of a received message is its tenant and its id, so a message whose key is
    /// already stored is ignored. A message with another tenant than the key of the store gives
    /// <see cref="Errors.ValidationFailed"/>, and the store adds nothing.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null for a success, else the error.</returns>
    ValueTask<Error?> AddAsync(ReceivedMessage message, CancellationToken ct);
}
