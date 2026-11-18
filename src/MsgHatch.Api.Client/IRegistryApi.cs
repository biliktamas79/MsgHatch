using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;
using MsgHatch.Http;

namespace MsgHatch.Api.Client;

/// <summary>
/// The GET routes of the registry API (section 4.20, D271). The generator writes <c>RegistryApi</c>, which sends each call
/// and gives each failure as an <see cref="Error"/> (D233, D236). The registry belongs to the whole system, so a route takes
/// no <c>tenantId</c> (D269).
/// </summary>
[HttpApi(typeof(RegistryEndpointJson))]
internal interface IRegistryApi
{
    [HttpCall("GET", RegistryApiRoutes.Channels)]
    Task<ErrorOr<IReadOnlyList<RegistryChannelResponse>>> GetChannelsAsync(CancellationToken ct);

    [HttpCall("GET", RegistryApiRoutes.ChannelHistory)]
    Task<ErrorOr<IReadOnlyList<ChannelStateResponse>>> GetChannelHistoryAsync(string channel, CancellationToken ct);

    [HttpCall("GET", RegistryApiRoutes.MessageTypes)]
    Task<ErrorOr<IReadOnlyList<RegistryMessageTypeResponse>>> GetMessageTypesAsync(CancellationToken ct);
}
