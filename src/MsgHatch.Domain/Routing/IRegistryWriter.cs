using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// Reads, saves, and resets the registry settings, only for an administration host (D117, D118, section 4.20). A setup
/// in code registers no writer. <c>UseStoredRegistry</c> registers one.
/// </summary>
public interface IRegistryWriter
{
    /// <summary>Reads the entries of the store, with their revisions.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entries.</returns>
    ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct);

    /// <summary>
    /// Saves the entry of a registered channel with the next revision and the time of the save. The
    /// <see cref="ChannelEntry.Revision"/> of the entry is the expected revision, and 0 means a new entry.
    /// </summary>
    /// <param name="entry">The entry with the expected revision.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// The saved entry, <see cref="Errors.NotFound"/> for an unregistered channel, <see cref="Errors.ValidationFailed"/>
    /// for an invalid entry, or <see cref="Errors.Conflict"/> when the stored revision is not the expected revision (D79).
    /// </returns>
    ValueTask<ErrorOr<ChannelEntry>> SaveChannelAsync(ChannelEntry entry, CancellationToken ct);

    /// <summary>
    /// Saves the entry of a registered message type with the next revision and the time of the save. The
    /// <see cref="MessageTypeEntry.Revision"/> of the entry is the expected revision, and 0 means a new entry.
    /// </summary>
    /// <param name="entry">The entry with the expected revision.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// The saved entry, <see cref="Errors.NotFound"/> for an unregistered message type, <see cref="Errors.ValidationFailed"/>
    /// for an invalid entry, or <see cref="Errors.Conflict"/> when the stored revision is not the expected revision (D79).
    /// </returns>
    ValueTask<ErrorOr<MessageTypeEntry>> SaveMessageTypeAsync(MessageTypeEntry entry, CancellationToken ct);

    /// <summary>Deletes the entry of a channel, so that the values of the code apply again.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or the error of the store.</returns>
    ValueTask<Error?> ResetChannelAsync(ChannelId channel, CancellationToken ct);

    /// <summary>Deletes the entry of a message type, so that the values of the code apply again.</summary>
    /// <param name="type">The message type.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or the error of the store.</returns>
    ValueTask<Error?> ResetMessageTypeAsync(MessageType type, CancellationToken ct);
}
