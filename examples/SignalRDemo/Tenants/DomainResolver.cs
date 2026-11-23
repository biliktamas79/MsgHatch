using Microsoft.Extensions.Options;

namespace SignalRDemo.Tenants;

/// <summary>
/// Maps the host of a page to its tenant and its company (D316): <c>{company}.localhost</c> gives a company and its
/// tenant, <c>{tenant}.localhost</c> gives only a tenant, and each other host gives nothing. A browser resolves each name
/// under <c>localhost</c> with no hosts file.
/// </summary>
/// <param name="data">The demo data.</param>
public sealed class DomainResolver(IOptions<DemoDataOptions> data)
{
    /// <summary>The route of the resolution endpoint.</summary>
    public const string Route = "/demo/resolve";

    private const string _localSuffix = ".localhost";

    /// <summary>Gives the tenant and the company of a host.</summary>
    /// <param name="host">The host, with or with no port, for example <c>acme.localhost:5091</c>.</param>
    /// <returns>The tenant and the company, each null when the host does not give it.</returns>
    public ResolvedDomain Resolve(string? host)
    {
        var label = LabelOf(host);
        if (label is null)
        {
            return ResolvedDomain.None;
        }

        if (data.Value.FindTenant(label) is { } tenant)
        {
            return new ResolvedDomain(tenant.Id, Company: null);
        }

        return data.Value.FindTenantOfCompany(label) is { } owner ? new ResolvedDomain(owner.Id, label) : ResolvedDomain.None;
    }

    private static string? LabelOf(string? host)
    {
        var name = new HostString(host ?? string.Empty).Host;
        return name.EndsWith(_localSuffix, StringComparison.OrdinalIgnoreCase) && name[..^_localSuffix.Length] is { Length: > 0 } label && !label.Contains('.', StringComparison.Ordinal)
            ? label
            : null;
    }
}

/// <summary>The tenant and the company that a host gives.</summary>
/// <param name="Tenant">The tenant, or null.</param>
/// <param name="Company">The company, or null.</param>
public sealed record ResolvedDomain(string? Tenant, string? Company)
{
    /// <summary>A host that gives no tenant and no company.</summary>
    public static readonly ResolvedDomain None = new(Tenant: null, Company: null);
}
