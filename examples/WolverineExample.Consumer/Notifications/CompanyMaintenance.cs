using WolverineExample.Contracts;

namespace WolverineExample.Consumer.Notifications;

/// <summary>
/// The event of the message type <c>TenantMaintenance</c> for one company of the tenant. A notification scope holds one
/// company or a list of users, so <see cref="DomainEventReceiver"/> accepts one of these events for each company.
/// </summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Maintenance">The maintenance of the tenant.</param>
public sealed record CompanyMaintenance(int CompanyId, TenantMaintenance Maintenance);
