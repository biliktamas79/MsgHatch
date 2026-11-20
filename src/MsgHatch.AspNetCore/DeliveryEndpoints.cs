using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.AspNetCore;

/// <summary>
/// The HTTP endpoints of the delivery management API (section 4.19, D54, D109). The overview and the actions are one
/// route group, and the content is a second route group, so that the host can give the content a stricter
/// authorization policy. The library adds no authorization: the host adds it on each returned group, for example with
/// <c>.RequireAuthorization("DeliveryAdmin")</c> and <c>.RequireAuthorization("DeliveryContent")</c>.
/// </summary>
public static class DeliveryEndpoints
{
    /// <summary>
    /// Maps the overview of <see cref="IDeliveryQueries"/> and the actions of <see cref="IDeliveryCommands"/> (D289): the
    /// channels with their pause and resume, the traffic, the pages of the inbox and the outbox, the detail of an outgoing
    /// message, the retry and the cancel of the messages of a query, the purge, and the delete of the test data with
    /// <c>DELETE {prefix}/test-data</c> (D291). Each route takes the optional query parameter <c>tenantId</c>, and uses the
    /// queries or the commands of <see cref="TenantKey.For"/> that tenant, or of <see cref="TenantKey.None"/>. The pause,
    /// resume, purge, and test data routes act on all tenants, so a <c>tenantId</c> gives 400. An error gives the problem
    /// details of D233, with the member <c>errorType</c>.
    /// </summary>
    /// <param name="routes">The endpoint route builder, for example the web application.</param>
    /// <param name="prefix">The path of the route group.</param>
    /// <returns>The route group.</returns>
    public static RouteGroupBuilder MapMsgHatchDelivery(this IEndpointRouteBuilder routes, string prefix = "/delivery")
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(prefix);
        var group = routes.MapGroup(prefix);
        DeliveryRoutes.MapOverviewTo(group);
        return group;
    }

    /// <summary>
    /// Maps the content routes of <see cref="IDeliveryQueries"/>: <c>GET {prefix}/inbox/{id}/content</c> gives the
    /// received message with its event, and <c>GET {prefix}/outbox/{id}/content</c> the outgoing message with its
    /// envelope, written with <c>MsgHatchJson</c> (D109). Each route takes the optional query parameter <c>tenantId</c>.
    /// </summary>
    /// <param name="routes">The endpoint route builder, for example the web application.</param>
    /// <param name="prefix">The path of the route group.</param>
    /// <returns>The route group.</returns>
    public static RouteGroupBuilder MapMsgHatchDeliveryContent(this IEndpointRouteBuilder routes, string prefix = "/delivery")
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(prefix);
        var group = routes.MapGroup(prefix);
        DeliveryRoutes.MapContentTo(group);
        return group;
    }
}
