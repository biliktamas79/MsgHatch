using MsgHatch.Domain.Identity;

namespace MsgHatch.Channels.SignalR;

/// <summary>
/// The SignalR group names of the companies and the tenants (D74, D114, D315). The host's hub must add each connection to
/// the group of the company of its user and to the group of its tenant, for example in <c>OnConnectedAsync</c>, because
/// the transport sends a company message and a tenant message to these groups.
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

    /// <summary>
    /// Gives the SignalR group name of all users of a tenant: <c>msghatch:tenant:{TenantId}</c>, or <c>msghatch:all</c>
    /// with no tenant. In the id, <c>%</c> becomes <c>%25</c> and <c>:</c> becomes <c>%3A</c>, so that the group of a
    /// tenant is never the group of another tenant or of a company.
    /// </summary>
    /// <param name="tenant">The tenant, or null for no tenant.</param>
    /// <returns>The group name.</returns>
    public static string ForTenant(TenantId? tenant) =>
        tenant is { } id ? $"msghatch:tenant:{SignalRNames.Escape(id.Value)}" : "msghatch:all";
}
