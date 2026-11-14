namespace WolverineExample.Contracts;

/// <summary>A company was archived.</summary>
/// <param name="TenantId">The tenant of the company.</param>
/// <param name="CompanyId">The archived company.</param>
/// <param name="ArchivedAtUtc">The time of the archive.</param>
public sealed record CompanyArchived(string TenantId, int CompanyId, DateTimeOffset ArchivedAtUtc);
