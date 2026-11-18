using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
    /// <see cref="IMessageRegistry"/> with the revision of each entry, through <c>IRegistryQueries</c> of <c>TenantKey.None</c>
    /// (D270). With an <see cref="IRegistryWriter"/>, for example
    /// from <c>UseStoredRegistry</c>, it also maps <c>PUT</c> and <c>DELETE {prefix}/channels/{channel}</c> and
    /// <c>{prefix}/message-types/{type}</c>. With the in-memory reader of a setup in code, it maps only the GET routes,
    /// and each revision is 0.
    /// </summary>
    /// <param name="routes">The endpoint route builder, for example the web application.</param>
    /// <param name="prefix">The path of the route group.</param>
    /// <returns>The route group.</returns>
    public static RouteGroupBuilder MapMsgHatchRegistry(this IEndpointRouteBuilder routes, string prefix = "/registry")
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(prefix);
        var group = routes.MapGroup(prefix);
        RegistryRoutes.MapTo(group, writable: routes.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(IRegistryWriter)) == true);
        return group;
    }
}
