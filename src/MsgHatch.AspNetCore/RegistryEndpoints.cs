using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AspNetCore;

/// <summary>
/// The HTTP endpoints of the registry administration (section 4.20, D117, D118). The library adds no authorization:
/// the host adds it on the returned group, for example with <c>.RequireAuthorization("RegistryAdmin")</c> (D170).
/// </summary>
public static class RegistryEndpoints
{
    /// <summary>
    /// Maps the registry routes. <c>GET {prefix}/channels</c> and <c>GET {prefix}/message-types</c> give the values of
    /// <see cref="IMessageRegistry"/> with the revision and the stored entry of each item, through <c>IRegistryQueries</c> of
    /// <c>TenantKey.None</c> (D270, D286). <c>POST {prefix}/channels/{channel}/check</c> runs the channel check of
    /// <c>IRegistryCommands</c> of <c>TenantKey.None</c> (D283). <c>PUT</c> and <c>DELETE {prefix}/channels/{channel}</c> and
    /// <c>{prefix}/message-types/{type}</c> save and reset the settings through the same commands (D284). With the
    /// in-memory reader of a setup in code, the host has no <see cref="IRegistryWriter"/>, so each revision is 0, and a save
    /// or a reset gives 404 with the problem body of the command.
    /// </summary>
    /// <param name="routes">The endpoint route builder, for example the web application.</param>
    /// <param name="prefix">The path of the route group.</param>
    /// <returns>The route group.</returns>
    public static RouteGroupBuilder MapMsgHatchRegistry(this IEndpointRouteBuilder routes, string prefix = "/registry")
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(prefix);
        var group = routes.MapGroup(prefix);
        RegistryRoutes.MapTo(group);
        return group;
    }
}
