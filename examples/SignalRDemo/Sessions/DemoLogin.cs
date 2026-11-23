using Microsoft.Extensions.Options;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using SignalRDemo.Tenants;

namespace SignalRDemo.Sessions;

/// <summary>
/// The demo login with no password (D316). It gives a token of the app for a user of the tenant of the page. A user with
/// accounts in more than one company of the tenant chooses one account, and a page with a company from its domain sends
/// that company as the account.
/// </summary>
public static class DemoLogin
{
    /// <summary>The route of the login endpoint.</summary>
    public const string Route = "/demo/login";

    /// <summary>Gives the account of a login request.</summary>
    /// <param name="data">The demo data.</param>
    /// <param name="request">The login request.</param>
    /// <returns>
    /// The account, <see cref="Errors.NotFound"/> for a user that the tenant does not have,
    /// <see cref="Errors.ValidationFailed"/> for a user with more than one account and no choice, or
    /// <see cref="Errors.Rejected"/> for an account that the user does not have in the tenant.
    /// </returns>
    public static ErrorOr<DemoAccount> FindAccount(DemoDataOptions data, LoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(request);
        var tenant = data.FindTenant(request.Tenant);
        if (tenant?.FindUser(request.User) is not { } user)
        {
            return new Error(Errors.NotFound, "The tenant has no such user.");
        }

        var company = request.Account ?? (user.Accounts is [var only] ? only : null);
        if (company is null)
        {
            return new Error(Errors.ValidationFailed, "The user has accounts in more than one company of the tenant. Choose one account.");
        }

        return user.Accounts.Contains(company)
            ? new DemoAccount(new TenantId(tenant.Id), new CompanyId(company), new UserId(user.Id))
            : new Error(Errors.Rejected, "The user has no account in this company of the tenant.");
    }

    internal static IResult Handle(LoginRequest request, IOptions<DemoDataOptions> data, DemoTokens tokens)
    {
        var account = FindAccount(data.Value, request);
        return account.IsError
            ? DemoProblems.Of(account.Error)
            : Results.Ok(new LoginResponse(tokens.Create(account.Value), account.Value.Tenant.Value, account.Value.Company.Value, account.Value.User.Value));
    }
}

/// <summary>A login request of a page.</summary>
/// <param name="Tenant">The tenant of the page.</param>
/// <param name="User">The user.</param>
/// <param name="Account">The company of the account, or null for the one account of the user in the tenant.</param>
public sealed record LoginRequest(string? Tenant, string? User, string? Account = null);

/// <summary>The token of a login, with its account.</summary>
/// <param name="Token">The token of the app.</param>
/// <param name="Tenant">The tenant.</param>
/// <param name="Company">The company of the account.</param>
/// <param name="User">The user.</param>
public sealed record LoginResponse(string Token, string Tenant, string Company, string User);
