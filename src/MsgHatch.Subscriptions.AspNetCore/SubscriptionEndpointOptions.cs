using System.Security.Claims;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.AspNetCore;

/// <summary>The options of <see cref="SubscriptionEndpoints.MapMsgHatchSubscriptions"/> (section 4.17, D154).</summary>
public sealed class SubscriptionEndpointOptions
{
    /// <summary>
    /// Gives the user id of the signed-in user, or null when the user has none. With a value, the endpoints also exist under
    /// <c>{prefix}/me</c>, for the user-level subscriptions of that user only. A null user id there gives 403.
    /// </summary>
    public Func<ClaimsPrincipal, UserId?>? CurrentUser { get; set; }

    /// <summary>
    /// Gives the tenant of the signed-in user for the <c>/me</c> routes, or null for no tenant. With no function, the
    /// <c>/me</c> routes use <see cref="TenantKey.None"/>. It needs <see cref="CurrentUser"/>.
    /// </summary>
    public Func<ClaimsPrincipal, TenantId?>? CurrentTenant { get; set; }
}
