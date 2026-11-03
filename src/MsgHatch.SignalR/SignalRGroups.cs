using MsgHatch.Domain.Identity;

namespace MsgHatch.SignalR;

/// <summary>
/// The SignalR group names of the companies (D74, D114). The host's hub must add each connection to the group of the
/// company of its user, for example in <c>OnConnectedAsync</c>, because the transport sends a company message to that group.
/// </summary>
public static class SignalRGroups
{
    /// <summary>
    /// Gives the SignalR group name of a company: <c>msghatch:company:{CompanyId}</c> with no tenant, and
    /// <c>msghatch:{TenantId}:company:{CompanyId}</c> with a tenant. In each id, <c>%</c> becomes <c>%25</c> and
    /// <c>:</c> becomes <c>%3A</c>, so that two companies never get the same group.
    /// </summary>
    /// <param name="tenant">The tenant of the company, or null for no tenant.</param>
    /// <param name="company">The company.</param>
    /// <returns>The group name.</returns>
    public static string ForCompany(TenantId? tenant, CompanyId company) =>
        tenant is { } id
            ? $"msghatch:{SignalRNames.Escape(id.Value)}:company:{SignalRNames.Escape(company.Value)}"
            : $"msghatch:company:{SignalRNames.Escape(company.Value)}";
}
