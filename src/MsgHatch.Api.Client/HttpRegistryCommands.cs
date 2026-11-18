using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Api.Client;

/// <summary>
/// The registry commands over HTTP (D227, D283). A <see cref="ChannelId"/> is valid by its type, so the check runs no rule
/// before the call. Each tenant key checks the same channels, because the registry belongs to the whole system (D269).
/// </summary>
/// <param name="api">The generated client of the routes.</param>
internal sealed class HttpRegistryCommands(RegistryApi api) : IRegistryCommands
{
    public async ValueTask<ErrorOr<ChannelCheck>> CheckChannelAsync(ChannelId channel, CancellationToken ct) =>
        ApiResults.One(await api.CheckChannelAsync(channel.Name, ct), static body => body.ToCheck());
}
