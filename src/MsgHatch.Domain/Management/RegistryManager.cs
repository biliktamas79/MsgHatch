using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The in-process registry queries (D268): the values of <see cref="IMessageRegistry"/>, joined with the revisions of
/// <see cref="IRegistryWriter.GetEntriesAsync"/> and with the state rows of <see cref="IOutboxDispatchStore"/> (D278). With
/// no writer, for example with the in-memory reader of a setup in code, each revision is 0. An error of the writer or of the
/// store is the error of the query.
/// </summary>
internal sealed class RegistryManager(IMessageRegistry registry, IRegistryWriter? writer, IOutboxDispatchStore outbox, TimeProvider time)
    : IRegistryQueries
{
    public static RegistryManager Create(IServiceProvider services) =>
        new(
            services.GetRequiredService<IMessageRegistry>(), services.GetService<IRegistryWriter>(),
            services.GetRequiredService<IOutboxDispatchStore>(), services.GetRequiredService<TimeProvider>());

    public async ValueTask<ErrorOr<IReadOnlyList<RegistryChannel>>> GetChannelsAsync(CancellationToken ct)
    {
        var revisions = await RevisionsAsync(ct);
        if (revisions.IsError)
        {
            return revisions.Error;
        }

        var rows = await RowsOfTheWindowAsync(ct);
        if (rows.IsError)
        {
            return rows.Error;
        }

        var latest = rows.Value.GroupBy(static row => row.Channel).ToDictionary(static group => group.Key, static group => group.MaxBy(static row => row.Version)!);
        return ErrorOr<IReadOnlyList<RegistryChannel>>.ForValue([.. registry.Channels.Select(channel => new RegistryChannel(
            channel.Id, channel.Name, channel.Description, channel.Features, channel.Enabled, revisions.Value.Channels.GetValueOrDefault(channel.Id),
            latest.GetValueOrDefault(channel.Id)))]);
    }

    public async ValueTask<ErrorOr<IReadOnlyList<RegistryMessageType>>> GetMessageTypesAsync(CancellationToken ct)
    {
        var revisions = await RevisionsAsync(ct);
        return revisions.IsError
            ? revisions.Error
            : ErrorOr<IReadOnlyList<RegistryMessageType>>.ForValue([.. registry.MessageTypes.Select(type => new RegistryMessageType(
                type.Type, type.DisplayName, type.Description, type.Channels, type.Enabled, type.DisabledChannels ?? [],
                revisions.Value.MessageTypes.GetValueOrDefault(type.Type)))]);
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

    private async ValueTask<ErrorOr<Revisions>> RevisionsAsync(CancellationToken ct)
    {
        if (writer is null)
        {
            return new Revisions([], []);
        }

        var entries = await writer.GetEntriesAsync(ct);
        return entries.IsError ? entries.Error : new Revisions(
            entries.Value.Channels.DistinctBy(static entry => entry.Channel).ToDictionary(static entry => entry.Channel, static entry => entry.Revision),
            entries.Value.MessageTypes.DistinctBy(static entry => entry.Type).ToDictionary(static entry => entry.Type, static entry => entry.Revision));
    }

    /// <summary>The state rows of the window of <see cref="ChannelState.HistoryWindow"/>, with the row of each channel that was current at its start.</summary>
    private ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> RowsOfTheWindowAsync(CancellationToken ct) =>
        outbox.GetChannelStatesAsync(time.GetUtcNowDateTime() - ChannelState.HistoryWindow, ct);

    private sealed record Revisions(Dictionary<ChannelId, int> Channels, Dictionary<MessageType, int> MessageTypes);
}
