using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Persistence;

/// <summary>
/// Lists the tenants that have their own storage, for a setup with one storage for each tenant (D125). The consumer
/// implements it. Only the dispatch composites call it, one time for each claim, count, delete, and purge. A caller must
/// not await one <see cref="ValueTask"/> two times.
/// </summary>
public interface ITenantSource
{
    /// <summary>Gives each tenant that has its own storage.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The tenants.</returns>
    ValueTask<ErrorOr<IReadOnlyList<TenantId>>> GetTenantsAsync(CancellationToken ct);
}
