namespace WolverineExample.Contracts;

/// <summary>A maintenance of a tenant is planned. It concerns all companies of the tenant.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="StartsAtUtc">The start of the maintenance.</param>
/// <param name="EndsAtUtc">The end of the maintenance.</param>
public sealed record TenantMaintenance(string TenantId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);
