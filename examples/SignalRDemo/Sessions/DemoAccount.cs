using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using MsgHatch.Domain.Identity;

namespace SignalRDemo.Sessions;

/// <summary>The signed-in account of a session: the tenant, the company of the account, and the user.</summary>
/// <param name="Tenant">The tenant, from the claim <c>tenant</c>.</param>
/// <param name="Company">The company of the account, from the claim <c>company</c>.</param>
/// <param name="User">The user, from the claim <c>sub</c>.</param>
public sealed record DemoAccount(TenantId Tenant, CompanyId Company, UserId User)
{
    /// <summary>The claim type of the tenant.</summary>
    public const string TenantClaim = "tenant";

    /// <summary>The claim type of the company.</summary>
    public const string CompanyClaim = "company";

    /// <summary>Gives the account of a token.</summary>
    /// <param name="principal">The caller.</param>
    /// <returns>The account, or null for an anonymous caller or a token with no valid tenant, company, or user.</returns>
    public static DemoAccount? Of(ClaimsPrincipal? principal)
    {
        var tenant = TenantId.Create(principal?.FindFirstValue(TenantClaim));
        var company = CompanyId.Create(principal?.FindFirstValue(CompanyClaim));
        var user = UserId.Create(principal?.FindFirstValue(JwtRegisteredClaimNames.Sub));
        return tenant.IsError || company.IsError || user.IsError ? null : new DemoAccount(tenant.Value, company.Value, user.Value);
    }
}
