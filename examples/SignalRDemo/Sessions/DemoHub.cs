using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Identity;
using MsgHatch.SignalR;
using SignalRDemo.Tenants;

namespace SignalRDemo.Sessions;

/// <summary>
/// The hub of the demo (D316). A connection with a token joins the groups of the tenant and of the company of its token.
/// An anonymous connection joins the group of the tenant of its page with <see cref="JoinTenantAsync"/>, and also the
/// group of the company of a tenant with one company. After each join, the hub tells the connection its tenant, company,
/// user, and groups with the client method <see cref="JoinedMethod"/>.
/// </summary>
/// <param name="data">The demo data.</param>
public sealed class DemoHub(IOptions<DemoDataOptions> data) : Hub
{
    /// <summary>The route of the hub.</summary>
    public const string Route = "/hubs/demo";

    /// <summary>The hub method of an anonymous connection that joins the group of a tenant.</summary>
    public const string JoinTenantMethod = "JoinTenant";

    /// <summary>The client method that gets the <see cref="JoinedSession"/> of a join.</summary>
    public const string JoinedMethod = "Joined";

    #region hub
    /// <inheritdoc/>
    public override async Task OnConnectedAsync()
    {
        if (DemoAccount.Of(Context.User) is { } account)
        {
            await JoinAsync(account.Tenant, account.Company, account.User);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Adds an anonymous connection to the group of a tenant, and to the group of the company of a tenant with one company.</summary>
    /// <param name="tenant">The tenant of the page.</param>
    /// <returns>The task of the join.</returns>
    /// <exception cref="HubException">The demo has no such tenant.</exception>
    [HubMethodName(JoinTenantMethod)]
    public Task JoinTenantAsync(string tenant)
    {
        var found = data.Value.FindTenant(tenant) ?? throw new HubException("The demo has no such tenant.");
        CompanyId? company = found.Companies is [var only] ? new CompanyId(only) : null;
        return JoinAsync(new TenantId(found.Id), company, user: null);
    }

    private async Task JoinAsync(TenantId tenant, CompanyId? company, UserId? user)
    {
        List<string> groups = [SignalRGroups.ForTenant(tenant)];
        if (company is { } id)
        {
            groups.Add(SignalRGroups.ForCompany(tenant, id));
        }

        foreach (var group in groups)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        }

        var session = new JoinedSession(
            tenant.Value, company?.Value, user?.Value, user is { } name ? SignalRUsers.ForUser(tenant, name) : null, groups);
        await Clients.Caller.SendAsync(JoinedMethod, session, Context.ConnectionAborted);
    }
    #endregion
}

/// <summary>What a connection joined: the data of its pane.</summary>
/// <param name="Tenant">The tenant.</param>
/// <param name="Company">The company, or null.</param>
/// <param name="User">The user, or null for an anonymous connection.</param>
/// <param name="UserName">The SignalR user name of the user, which <see cref="DemoUserIdProvider"/> gives, or null.</param>
/// <param name="Groups">The SignalR groups of the connection.</param>
public sealed record JoinedSession(string Tenant, string? Company, string? User, string? UserName, IReadOnlyList<string> Groups);
