using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The in-process registry queries (D268): the values of <see cref="IMessageRegistry"/>, joined with the entries of
/// <see cref="IRegistryWriter.GetEntriesAsync"/> (D286) and with the state rows of <see cref="IOutboxDispatchStore"/> (D278).
/// With no writer, for example with the in-memory reader of a setup in code, each revision is 0 and each entry is null. An error of the writer or of the
/// store is the error of the query. The channel check runs the health check of the transport of the host, and records a
/// new state with the store methods of D277, as the outbox loop does (D283).
/// </summary>
internal sealed class RegistryManager(
    IMessageRegistry registry, IRegistryWriter? writer, IOutboxDispatchStore outbox, IEnumerable<ITransport> transports,
    IOptionsMonitor<ChannelDownOptions> downOptions, IDispatchSignal signal, TimeProvider time)
    : IRegistryQueries, IRegistryCommands
{
    public static RegistryManager Create(IServiceProvider services) =>
        new(
            services.GetRequiredService<IMessageRegistry>(), services.GetService<IRegistryWriter>(),
            services.GetRequiredService<IOutboxDispatchStore>(), services.GetServices<ITransport>(),
            services.GetRequiredService<IOptionsMonitor<ChannelDownOptions>>(), services.GetRequiredService<IDispatchSignal>(),
            services.GetRequiredService<TimeProvider>());

    public async ValueTask<ErrorOr<IReadOnlyList<RegistryChannel>>> GetChannelsAsync(CancellationToken ct)
    {
        var entries = await EntriesAsync(ct);
        if (entries.IsError)
        {
            return entries.Error;
        }

        var rows = await RowsOfTheWindowAsync(ct);
        if (rows.IsError)
        {
            return rows.Error;
        }

        var latest = rows.Value.GroupBy(static row => row.Channel).ToDictionary(static group => group.Key, static group => group.MaxBy(static row => row.Version)!);
        return ErrorOr<IReadOnlyList<RegistryChannel>>.ForValue([.. registry.Channels.Select(channel =>
        {
            var entry = entries.Value.Channels.GetValueOrDefault(channel.Id);
            return new RegistryChannel(
                channel.Id, channel.Name, channel.Description, channel.Features, channel.Enabled, entry?.Revision ?? 0,
                latest.GetValueOrDefault(channel.Id), entry);
        })]);
    }

    public async ValueTask<ErrorOr<IReadOnlyList<RegistryMessageType>>> GetMessageTypesAsync(CancellationToken ct)
    {
        var entries = await EntriesAsync(ct);
        return entries.IsError
            ? entries.Error
            : ErrorOr<IReadOnlyList<RegistryMessageType>>.ForValue([.. registry.MessageTypes.Select(type =>
            {
                var entry = entries.Value.MessageTypes.GetValueOrDefault(type.Type);
                return new RegistryMessageType(
                    type.Type, type.DisplayName, type.Description, type.Channels, type.Enabled, type.DisabledChannels ?? [], entry?.Revision ?? 0,
                    entry);
            })]);
    }

    public async ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelHistoryAsync(ChannelId channel, CancellationToken ct)
    {
        if (registry.FindChannel(channel) is null)
        {
            return new Error(Errors.NotFound, "The channel is not registered.");
        }

        var rows = await RowsOfTheWindowAsync(ct);
        return rows.IsError
            ? rows.Error
            : ErrorOr<IReadOnlyList<ChannelState>>.ForValue([.. rows.Value.Where(row => row.Channel == channel)]);
    }

    public async ValueTask<ErrorOr<ChannelCheck>> CheckChannelAsync(ChannelId channel, CancellationToken ct)
    {
        if (registry.FindChannel(channel) is null)
        {
            return new Error(Errors.NotFound, "The channel is not registered.");
        }

        if (transports.FirstOrDefault(transport => transport.Channel == channel) is not { } transport)
        {
            return new Error(Errors.NotFound, "This host has no transport of the channel.");
        }

        var health = await transport.CheckHealthAsync(ct);
        if (health is { Type: var type } && type == Errors.OperationCancelled)
        {
            return health;
        }

        var changed = health is null ? await MarkUpAsync(channel, ct) : await MarkDownAsync(channel, health, ct);
        return changed.IsError ? changed.Error : new ChannelCheck(channel, health?.Type.Code, health?.Type.Name, changed.Value);
    }

    /// <summary>Ends a down state for errors, and wakes the outbox loop of the channel when the store added the up row.</summary>
    private async ValueTask<ErrorOr<bool>> MarkUpAsync(ChannelId channel, CancellationToken ct)
    {
        var up = await outbox.SetChannelUpAsync(channel, ChannelDownReason.Errors, time.GetUtcNowDateTime(), ct);
        if (up is { IsSuccess: true, Value: true })
        {
            _ = await signal.NotifyAsync(DispatchTopic.ForChannel(channel), ct);
        }

        return up;
    }

    private ValueTask<ErrorOr<bool>> MarkDownAsync(ChannelId channel, Error health, CancellationToken ct)
    {
        var nowUtc = time.GetUtcNowDateTime();
        var down = new ChannelDown(
            channel, ChannelDownReason.Errors, nowUtc, nowUtc + downOptions.Get(channel.Name).CheckInterval, health.Type.Code,
            health.Type.Name, Note: null);
        return outbox.SetChannelDownAsync(down, ct);
    }

    /// <summary>The stored entries of the writer by channel and by message type, or no entry for a host with no writer (D286).</summary>
    private async ValueTask<ErrorOr<Entries>> EntriesAsync(CancellationToken ct)
    {
        if (writer is null)
        {
            return new Entries([], []);
        }

        var entries = await writer.GetEntriesAsync(ct);
        return entries.IsError ? entries.Error : new Entries(
            entries.Value.Channels.DistinctBy(static entry => entry.Channel).ToDictionary(static entry => entry.Channel),
            entries.Value.MessageTypes.DistinctBy(static entry => entry.Type).ToDictionary(static entry => entry.Type));
    }

    /// <summary>The state rows of the window of <see cref="ChannelState.HistoryWindow"/>, with the row of each channel that was current at its start.</summary>
    private ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> RowsOfTheWindowAsync(CancellationToken ct) =>
        outbox.GetChannelStatesAsync(time.GetUtcNowDateTime() - ChannelState.HistoryWindow, ct);

    private sealed record Entries(Dictionary<ChannelId, ChannelEntry> Channels, Dictionary<MessageType, MessageTypeEntry> MessageTypes);
}
