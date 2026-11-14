using System.Security.Claims;
using MsgHatch.Domain.Identity;

namespace WolverineExample.Consumer.Channels;

/// <summary>The tenant and the user of a SignalR connection, from the claims of its token.</summary>
/// <param name="Tenant">The tenant, from the claim <c>tenant</c>.</param>
/// <param name="User">The user, from the claim that <c>dotnet user-jwts create --name</c> sets (D158).</param>
public sealed record HubCaller(TenantId Tenant, UserId User)
{
    /// <summary>The claim type of the tenant.</summary>
    public const string TenantClaim = "tenant";

    /// <summary>Gives the caller of a token.</summary>
    /// <param name="principal">The signed-in caller.</param>
    /// <returns>The caller, or null when the token has no valid tenant or no valid user.</returns>
    public static HubCaller? Of(ClaimsPrincipal? principal)
    {
        var tenant = TenantId.Create(principal?.FindFirstValue(TenantClaim));
        var user = UserId.Create(principal?.FindFirstValue(ClaimTypes.NameIdentifier));
        return tenant.IsError || user.IsError ? null : new HubCaller(tenant.Value, user.Value);
    }
}
