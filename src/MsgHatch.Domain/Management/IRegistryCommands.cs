using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The commands of the registry administration (D231, D283, D284). The registry belongs to the whole system, so the
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

    /// <summary>
    /// Saves the stored settings of a registered channel (D284). The <see cref="ChannelEntry.Revision"/> of the entry is the
    /// expected revision, and 0 means a new entry (D79). The save ignores <see cref="ChannelEntry.LastModifiedAtUtc"/>.
    /// </summary>
    /// <param name="entry">The entry with the expected revision.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// The saved entry with its new revision, <see cref="Errors.ValidationFailed"/> for an entry that breaks
    /// <see cref="RegistryEntryRules"/>, <see cref="Errors.NotFound"/> for a channel that is not registered or for a host with
    /// no registry writer, <see cref="Errors.Conflict"/> when the stored revision is not the expected revision, or the error
    /// of the writer.
    /// </returns>
    ValueTask<ErrorOr<ChannelEntry>> SaveChannelAsync(ChannelEntry entry, CancellationToken ct);

    /// <summary>
    /// Saves the stored settings of a registered message type (D284). The <see cref="MessageTypeEntry.Revision"/> of the
    /// entry is the expected revision, and 0 means a new entry (D79). The save ignores
    /// <see cref="MessageTypeEntry.LastModifiedAtUtc"/>.
    /// </summary>
    /// <param name="entry">The entry with the expected revision.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// The saved entry with its new revision, <see cref="Errors.ValidationFailed"/> for an entry that breaks
    /// <see cref="RegistryEntryRules"/> or that disables a channel that the message type does not register,
    /// <see cref="Errors.NotFound"/> for a message type that is not registered or for a host with no registry writer,
    /// <see cref="Errors.Conflict"/> when the stored revision is not the expected revision, or the error of the writer.
    /// </returns>
    ValueTask<ErrorOr<MessageTypeEntry>> SaveMessageTypeAsync(MessageTypeEntry entry, CancellationToken ct);

    /// <summary>Deletes the stored settings of a channel, so that the values of the code apply again (D284).</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, <see cref="Errors.NotFound"/> for a host with no registry writer, or the error of the writer.</returns>
    ValueTask<Error?> ResetChannelAsync(ChannelId channel, CancellationToken ct);

    /// <summary>Deletes the stored settings of a message type, so that the values of the code apply again (D284).</summary>
    /// <param name="type">The message type.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, <see cref="Errors.NotFound"/> for a host with no registry writer, or the error of the writer.</returns>
    ValueTask<Error?> ResetMessageTypeAsync(MessageType type, CancellationToken ct);
}
