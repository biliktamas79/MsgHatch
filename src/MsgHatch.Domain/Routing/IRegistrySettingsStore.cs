using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// Keeps the registry settings of the whole system, not of a tenant (D114, D118). A save writes the entry only when
/// the stored revision is the revision of the entry minus 1, and a missing entry has the revision 0. Else it gives
/// <see cref="Errors.Conflict"/>. A delete of an unknown entry succeeds (section 4.6).
/// </summary>
public interface IRegistrySettingsStore
{
    /// <summary>Reads all entries.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entries, each list in the ordinal order of the names.</returns>
    ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct);

    /// <summary>Writes a channel entry when the stored revision is <c>entry.Revision - 1</c>.</summary>
    /// <param name="entry">The entry with its new revision.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or <see cref="Errors.Conflict"/> for another stored revision.</returns>
    ValueTask<Error?> SaveChannelEntryAsync(ChannelEntry entry, CancellationToken ct);

    /// <summary>Writes a message type entry when the stored revision is <c>entry.Revision - 1</c>.</summary>
    /// <param name="entry">The entry with its new revision.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or <see cref="Errors.Conflict"/> for another stored revision.</returns>
    ValueTask<Error?> SaveMessageTypeEntryAsync(MessageTypeEntry entry, CancellationToken ct);

    /// <summary>Deletes the entry of a channel. A delete of an unknown entry succeeds.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or the error of the storage.</returns>
    ValueTask<Error?> DeleteChannelEntryAsync(ChannelId channel, CancellationToken ct);

    /// <summary>Deletes the entry of a message type. A delete of an unknown entry succeeds.</summary>
    /// <param name="type">The message type.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or the error of the storage.</returns>
    ValueTask<Error?> DeleteMessageTypeEntryAsync(MessageType type, CancellationToken ct);
}
