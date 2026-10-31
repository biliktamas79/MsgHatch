namespace MsgHatch.Domain.Identity;

/// <summary>
/// The key of a tenant service (D126). <see cref="None"/> is the key of the data with no tenant.
/// Two keys are equal when their tenants are equal.
/// </summary>
public readonly record struct TenantKey
{
    private TenantKey(TenantId? tenant)
    {
        Tenant = tenant;
    }

    /// <summary>The tenant, or null for no tenant.</summary>
    public TenantId? Tenant { get; }

    /// <summary>The key of the data with no tenant.</summary>
    public static TenantKey None { get; }

    /// <summary>Gives the key of a tenant.</summary>
    /// <param name="tenant">The tenant, or null for no tenant.</param>
    /// <returns>The key. A null tenant gives <see cref="None"/>.</returns>
    public static TenantKey For(TenantId? tenant) => new(tenant);
}
