using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// The registry of the code with the stored settings on top (D118, section 4.20). A read uses a frozen snapshot of the
/// descriptors of the code, merged with the entries of the store, and calls no store. The registry starts its first
/// read when the container creates it (D168), reads the store again after each refresh interval, and swaps the
/// snapshot after each successful read and after each own write. A store error keeps the last snapshot.
/// </summary>
// ponytail: a refresh interval in each process, a shared cache or a change signal if a change must reach all processes at once
internal sealed class StoredRegistry : IMessageRegistry, IRegistryWriter, IDisposable
{
    private readonly InMemoryMessageRegistry _code;
    private readonly IRegistrySettingsStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<StoredRegistry> _logger;
    private readonly Lock _lock = new();
    private readonly ITimer _timer;
    private RegistryEntries _entries = new([], []);
    private volatile InMemoryMessageRegistry _snapshot;
    private int _ownWrites;
    private int _reading;

    public StoredRegistry(
        InMemoryMessageRegistry code, IRegistrySettingsStore store, IOptions<RegistryOptions> options, TimeProvider time,
        ILogger<StoredRegistry> logger)
    {
        _code = code;
        _store = store;
        _time = time;
        _logger = logger;
        _snapshot = code;
        FirstRead = RefreshAsync();
        var interval = options.Value.RefreshInterval;
        _timer = time.CreateTimer(static registry => _ = ((StoredRegistry)registry!).RefreshAsync(), this, interval, interval);
    }

    /// <summary>The first read of the store. It completes with success or after a logged error, and never fails (D168).</summary>
    public Task FirstRead { get; }

    public IReadOnlyList<ChannelDescriptor> Channels => _snapshot.Channels;

    public IReadOnlyList<MessageTypeDescriptor> MessageTypes => _snapshot.MessageTypes;

    public ChannelDescriptor? FindChannel(ChannelId channel) => _snapshot.FindChannel(channel);

    public MessageTypeDescriptor? FindMessageType(MessageType type) => _snapshot.FindMessageType(type);

    public ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct) => _store.GetEntriesAsync(ct);

    public async ValueTask<ErrorOr<ChannelEntry>> SaveChannelAsync(ChannelEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_code.FindChannel(entry.Channel) is null)
        {
            return new Error(Errors.NotFound, $"The channel {entry.Channel.Name} is not registered.");
        }

        if (RegistryEntryRules.Check(entry) is { } invalid)
        {
            return invalid;
        }

        var saved = entry with { Revision = entry.Revision + 1, LastModifiedAtUtc = _time.GetUtcNowDateTime() };
        var error = await _store.SaveChannelEntryAsync(saved, ct);
        if (error is not null)
        {
            return error;
        }

        ApplyOwnWrite(entries => entries with { Channels = [.. WithoutChannel(entries, saved.Channel), saved] });
        return saved;
    }

    public async ValueTask<ErrorOr<MessageTypeEntry>> SaveMessageTypeAsync(MessageTypeEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_code.FindMessageType(entry.Type) is not { } registered)
        {
            return new Error(Errors.NotFound, $"The message type {entry.Type.Name} is not registered.");
        }

        if ((RegistryEntryRules.Check(entry) ?? CheckChannelsOf(registered, entry.DisabledChannels)) is { } invalid)
        {
            return invalid;
        }

        var saved = entry with { Revision = entry.Revision + 1, LastModifiedAtUtc = _time.GetUtcNowDateTime() };
        var error = await _store.SaveMessageTypeEntryAsync(saved, ct);
        if (error is not null)
        {
            return error;
        }

        ApplyOwnWrite(entries => entries with { MessageTypes = [.. WithoutMessageType(entries, saved.Type), saved] });
        return saved;
    }

    public async ValueTask<Error?> ResetChannelAsync(ChannelId channel, CancellationToken ct)
    {
        var error = await _store.DeleteChannelEntryAsync(channel, ct);
        if (error is null)
        {
            ApplyOwnWrite(entries => entries with { Channels = [.. WithoutChannel(entries, channel)] });
        }

        return error;
    }

    public async ValueTask<Error?> ResetMessageTypeAsync(MessageType type, CancellationToken ct)
    {
        var error = await _store.DeleteMessageTypeEntryAsync(type, ct);
        if (error is null)
        {
            ApplyOwnWrite(entries => entries with { MessageTypes = [.. WithoutMessageType(entries, type)] });
        }

        return error;
    }

    public void Dispose() => _timer.Dispose();

    /// <summary>
    /// Reads the store, and swaps the snapshot when no own write came after the start of the read. A read while
    /// another read runs does nothing, so an older read never replaces a newer one.
    /// </summary>
    private async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _reading, 1) == 1)
        {
            return;
        }

        try
        {
            var ownWritesAtStart = Volatile.Read(ref _ownWrites);
            var read = await Boundary.RunAsync(() => _store.GetEntriesAsync(CancellationToken.None), CancellationToken.None);
            if (read.IsError)
            {
                _logger.RefreshFailed(read.Error.Type.Code, read.Error.Type.Name, read.Error.Message);
                return;
            }

            lock (_lock)
            {
                if (_ownWrites == ownWritesAtStart)
                {
                    Swap(read.Value);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    private void ApplyOwnWrite(Func<RegistryEntries, RegistryEntries> change)
    {
        lock (_lock)
        {
            _ownWrites++;
            Swap(change(_entries));
        }
    }

    private void Swap(RegistryEntries entries)
    {
        _entries = entries;
        _snapshot = Merge(_code, entries);
    }

    /// <summary>The rule of an entry that needs the registry: each disabled channel is a channel of the message type (D285).</summary>
    private static Error? CheckChannelsOf(MessageTypeDescriptor registered, IReadOnlyList<ChannelId> disabledChannels) =>
        disabledChannels.Any(channel => !registered.Channels.Contains(channel))
            ? new Error(Errors.ValidationFailed, $"DisabledChannels has only the channels of the message type {registered.Type.Name}.")
            : null;

    private static IEnumerable<ChannelEntry> WithoutChannel(RegistryEntries entries, ChannelId channel) =>
        entries.Channels.Where(entry => entry.Channel != channel);

    private static IEnumerable<MessageTypeEntry> WithoutMessageType(RegistryEntries entries, MessageType type) =>
        entries.MessageTypes.Where(entry => entry.Type != type);

    /// <summary>Merges the descriptors of the code with the entries. The entry wins for each value that it has.</summary>
    private static InMemoryMessageRegistry Merge(InMemoryMessageRegistry code, RegistryEntries entries)
    {
        var channels = entries.Channels.DistinctBy(static entry => entry.Channel).ToDictionary(static entry => entry.Channel);
        var types = entries.MessageTypes.DistinctBy(static entry => entry.Type).ToDictionary(static entry => entry.Type);
        return new InMemoryMessageRegistry(
            code.Channels.Select(descriptor => channels.TryGetValue(descriptor.Id, out var entry)
                ? descriptor with
                {
                    Name = entry.DisplayName ?? descriptor.Name,
                    Description = entry.Description ?? descriptor.Description,
                    Enabled = entry.Enabled,
                }
                : descriptor),
            code.MessageTypes.Select(descriptor => types.TryGetValue(descriptor.Type, out var entry)
                ? descriptor with
                {
                    DisplayName = entry.DisplayName ?? descriptor.DisplayName,
                    Description = entry.Description ?? descriptor.Description,
                    Enabled = entry.Enabled,
                    DisabledChannels = entry.DisabledChannels,
                }
                : descriptor));
    }
}
