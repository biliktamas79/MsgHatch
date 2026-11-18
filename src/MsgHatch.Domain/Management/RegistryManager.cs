using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The in-process registry queries (D268): the values of <see cref="IMessageRegistry"/>, joined with the revisions of
/// <see cref="IRegistryWriter.GetEntriesAsync"/>. With no writer, for example with the in-memory reader of a setup in
/// code, each revision is 0. An error of the writer is the error of the query.
/// </summary>
internal sealed class RegistryManager(IMessageRegistry registry, IRegistryWriter? writer) : IRegistryQueries
{
    public static RegistryManager Create(IServiceProvider services) =>
        new(services.GetRequiredService<IMessageRegistry>(), services.GetService<IRegistryWriter>());

    public async ValueTask<ErrorOr<IReadOnlyList<RegistryChannel>>> GetChannelsAsync(CancellationToken ct)
    {
        var revisions = await RevisionsAsync(ct);
        return revisions.IsError
            ? revisions.Error
            : ErrorOr<IReadOnlyList<RegistryChannel>>.ForValue([.. registry.Channels.Select(channel => new RegistryChannel(
                channel.Id, channel.Name, channel.Description, channel.Features, channel.Enabled,
                revisions.Value.Channels.GetValueOrDefault(channel.Id)))]);
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

    private sealed record Revisions(Dictionary<ChannelId, int> Channels, Dictionary<MessageType, int> MessageTypes);
}
