using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Api.Client;

/// <summary>
/// The registry queries over HTTP (D227, D271). A query has no input, so it runs no rule before the call. Each tenant key
/// gets the same values, because the registry belongs to the whole system (D269).
/// </summary>
/// <param name="api">The generated client of the routes.</param>
internal sealed class HttpRegistryQueries(RegistryApi api) : IRegistryQueries
{
    public async ValueTask<ErrorOr<IReadOnlyList<RegistryChannel>>> GetChannelsAsync(CancellationToken ct) =>
        ApiResults.All(await api.GetChannelsAsync(ct), static channel => channel.ToChannel());

    public async ValueTask<ErrorOr<IReadOnlyList<RegistryMessageType>>> GetMessageTypesAsync(CancellationToken ct) =>
        ApiResults.All(await api.GetMessageTypesAsync(ct), static type => type.ToMessageType());

    public ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelHistoryAsync(ChannelId channel, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelState>>.ForValue([]));
}
