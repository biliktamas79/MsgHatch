using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// The registry settings in memory, for tests, demos, and a host with no database (D119). <c>UseInMemoryStores</c>
/// registers it. It is not durable: a restart loses each entry.
/// </summary>
// ponytail: not durable and for one process, use a storage package for production
public sealed class InMemoryRegistrySettingsStore : IRegistrySettingsStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<ChannelId, ChannelEntry> _channels = [];
    private readonly Dictionary<MessageType, MessageTypeEntry> _messageTypes = [];

    /// <inheritdoc />
    public ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(ErrorOr<RegistryEntries>.ForValue(new RegistryEntries(
                [.. _channels.Values.OrderBy(static entry => entry.Channel.Name, StringComparer.Ordinal)],
                [.. _messageTypes.Values.OrderBy(static entry => entry.Type.Name, StringComparer.Ordinal)])));
        }
    }

    /// <inheritdoc />
    public ValueTask<Error?> SaveChannelEntryAsync(ChannelEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ValueTask.FromResult(Save(_channels, entry.Channel, entry, entry.Revision, static stored => stored.Revision));
    }

    /// <inheritdoc />
    public ValueTask<Error?> SaveMessageTypeEntryAsync(MessageTypeEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ValueTask.FromResult(Save(
            _messageTypes, entry.Type, entry with { DisabledChannels = [.. entry.DisabledChannels] }, entry.Revision,
            static stored => stored.Revision));
    }

    /// <inheritdoc />
    public ValueTask<Error?> DeleteChannelEntryAsync(ChannelId channel, CancellationToken ct) => Delete(_channels, channel);

    /// <inheritdoc />
    public ValueTask<Error?> DeleteMessageTypeEntryAsync(MessageType type, CancellationToken ct) => Delete(_messageTypes, type);

    private Error? Save<TKey, TEntry>(Dictionary<TKey, TEntry> entries, TKey key, TEntry entry, int revision, Func<TEntry, int> revisionOf)
        where TKey : notnull
    {
        lock (_lock)
        {
            var stored = entries.TryGetValue(key, out var current) ? revisionOf(current) : 0;
            if (stored != revision - 1)
            {
                return new Error(Errors.Conflict, $"The stored revision is {stored}, not the revision before {revision}.");
            }

            entries[key] = entry;
            return null;
        }
    }

    private ValueTask<Error?> Delete<TKey, TEntry>(Dictionary<TKey, TEntry> entries, TKey key)
        where TKey : notnull
    {
        lock (_lock)
        {
            entries.Remove(key);
            return ValueTask.FromResult<Error?>(null);
        }
    }
}
