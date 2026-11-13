using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>The tenants of a host with one storage for each tenant, as a singleton for <see cref="FixedTenantSource"/>.</summary>
public sealed record FixedTenants(IReadOnlyList<TenantId> Ids);

/// <summary>A tenant source that gives the tenants of the registered <see cref="FixedTenants"/>.</summary>
public sealed class FixedTenantSource(FixedTenants tenants) : ITenantSource
{
    public ValueTask<ErrorOr<IReadOnlyList<TenantId>>> GetTenantsAsync(CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<TenantId>>.ForValue(tenants.Ids));
}
