using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Management;

namespace MsgHatch.Subscriptions.AspNetCore;

/// <summary>The HTTP endpoints of the subscription management API (section 4.17, D54).</summary>
public static class SubscriptionEndpoints
{
    /// <summary>
    /// Maps the minimal API endpoints of <see cref="ISubscriptionQueries"/> and <see cref="ISubscriptionCommands"/>: the
    /// channels, the message types, and the list, read, create, update, and delete of the subscriptions (D231). It also maps
    /// <c>POST {prefix}/verification</c> of <see cref="Verification.IRoutingVerifier"/>, only with no <c>/me</c> (D163). Each
    /// route takes the optional query parameter <c>tenantId</c>, and uses the services of <see cref="TenantKey.For"/> that tenant, or of <see cref="TenantKey.None"/>.
    /// With <see cref="SubscriptionEndpointOptions.CurrentUser"/>, the same endpoints also exist under <c>{prefix}/me</c>
    /// for the user-level subscriptions of the signed-in user (D154). The library adds no authorization, so the consumer
    /// adds it on the returned group, for example with <c>.RequireAuthorization("SubscriptionAdmin")</c>.
    /// </summary>
    /// <param name="routes">The endpoint route builder, for example the web application.</param>
    /// <param name="prefix">The path of the route group.</param>
    /// <param name="configure">An optional change of the options.</param>
    /// <returns>The route group.</returns>
    /// <exception cref="ArgumentException">The options have <c>CurrentTenant</c> with no <c>CurrentUser</c>.</exception>
    public static RouteGroupBuilder MapMsgHatchSubscriptions(
        this IEndpointRouteBuilder routes, string prefix = "/subscriptions", Action<SubscriptionEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(prefix);
        var options = new SubscriptionEndpointOptions();
        configure?.Invoke(options);
        if (options.CurrentTenant is not null && options.CurrentUser is null)
        {
            throw new ArgumentException("CurrentTenant needs CurrentUser, because only the /me routes use it.", nameof(configure));
        }

        var group = routes.MapGroup(prefix);
        new SubscriptionRoutes(signedInUser: null, signedInTenant: null).MapTo(group);
        SubscriptionRoutes.MapVerificationTo(group);
        if (options.CurrentUser is { } currentUser)
        {
            new SubscriptionRoutes(currentUser, options.CurrentTenant).MapTo(group.MapGroup(SubscriptionApiRoutes.Me));
        }

        return group;
    }
}
