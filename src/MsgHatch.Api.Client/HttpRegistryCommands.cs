using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Api.Client;

/// <summary>
/// The registry commands over HTTP (D227, D283, D284). A <see cref="ChannelId"/> is valid by its type, so the check and a
/// reset run no rule before the call. Each tenant key acts on the same registry, because the registry belongs to the whole
/// system (D269).
/// </summary>
/// <param name="api">The generated client of the routes.</param>
internal sealed class HttpRegistryCommands(RegistryApi api) : IRegistryCommands
{
    public async ValueTask<ErrorOr<ChannelCheck>> CheckChannelAsync(ChannelId channel, CancellationToken ct) =>
        ApiResults.One(await api.CheckChannelAsync(channel.Name, ct), static body => body.ToCheck());

    public async ValueTask<ErrorOr<ChannelEntry>> SaveChannelAsync(ChannelEntry entry, CancellationToken ct) =>
        ApiResults.One(
            await api.SaveChannelAsync(entry.Channel.Name, new ChannelEntryRequest(entry.Enabled, entry.Revision, entry.DisplayName, entry.Description), ct),
            static body => body.ToEntry());

    public async ValueTask<ErrorOr<MessageTypeEntry>> SaveMessageTypeAsync(MessageTypeEntry entry, CancellationToken ct) =>
        ApiResults.One(
            await api.SaveMessageTypeAsync(
                entry.Type.Name,
                new MessageTypeEntryRequest(
                    entry.Enabled, entry.Revision, entry.DisplayName, entry.Description, [.. entry.DisabledChannels.Select(static channel => channel.Name)]),
                ct),
            static body => body.ToEntry());

    public async ValueTask<Error?> ResetChannelAsync(ChannelId channel, CancellationToken ct) => await api.ResetChannelAsync(channel.Name, ct);

    public async ValueTask<Error?> ResetMessageTypeAsync(MessageType type, CancellationToken ct) => await api.ResetMessageTypeAsync(type.Name, ct);
}
