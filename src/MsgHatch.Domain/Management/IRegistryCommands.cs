using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The commands of the registry administration (D231, D283). The registry belongs to the whole system, so the
/// implementation of each tenant key acts on the same channels (D269). A caller must not await one returned
/// <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface IRegistryCommands
{
    /// <summary>
    /// Runs the health check of the transport of a channel in this host, and records a new state of the channel (D277). A
    /// check with no error brings a channel that is down for errors up, and a failed check marks an up channel down with the
    /// reason <see cref="ChannelDownReason.Errors"/>. A check that finds the same state adds no state row, and a check never
    /// ends a pause (D106).
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// The result of the check, <see cref="Errors.NotFound"/> for a channel that is not registered or that has no transport
    /// in this host, <see cref="Errors.OperationCancelled"/> for a cancelled check, or the error of the store.
    /// </returns>
    ValueTask<ErrorOr<ChannelCheck>> CheckChannelAsync(ChannelId channel, CancellationToken ct);
}
