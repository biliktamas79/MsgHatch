using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace SignalRDemo.Tenants;

/// <summary>
/// The tenants, the companies, and the users of the demo, from the section <c>Demo</c> of <c>demo-data.json</c>. The
/// host <c>{label}.localhost</c> of a page names a tenant or a company (<see cref="DomainResolver"/>), so each label
/// names one tenant or one company only. Each account of a user is a company of its tenant.
/// </summary>
public sealed class DemoDataOptions : IValidatableObject
{
    /// <summary>The configuration section of the options.</summary>
    public const string SectionName = "Demo";

    /// <summary>The tenants.</summary>
    [Required]
    [MinLength(1)]
    [ValidateEnumeratedItems]
    public List<DemoTenant> Tenants { get; set; } = [];

    /// <summary>Gives a tenant.</summary>
    /// <param name="id">The id of the tenant.</param>
    /// <returns>The tenant, or null when the demo has no such tenant.</returns>
    public DemoTenant? FindTenant(string? id) => Tenants.Find(tenant => tenant.Id == id);

    /// <summary>Gives the tenant of a company.</summary>
    /// <param name="company">The id of the company.</param>
    /// <returns>The tenant, or null when no tenant has the company.</returns>
    public DemoTenant? FindTenantOfCompany(string company) => Tenants.Find(tenant => tenant.Companies.Contains(company));

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var labels = Tenants.Select(static tenant => tenant.Id).Concat(Tenants.SelectMany(static tenant => tenant.Companies));
        foreach (var label in labels.CountBy(static label => label, StringComparer.Ordinal).Where(static label => label.Value > 1))
        {
            yield return new ValidationResult($"The host {label.Key}.localhost names more than one tenant or company.", [nameof(Tenants)]);
        }

        foreach (var tenant in Tenants)
        {
            foreach (var user in tenant.Users.Where(user => !user.Accounts.TrueForAll(tenant.Companies.Contains)))
            {
                yield return new ValidationResult($"The user {user.Id} of the tenant {tenant.Id} has an account in a company of another tenant.", [nameof(Tenants)]);
            }
        }
    }
}

/// <summary>One tenant of the demo. User ids and company ids are unique only inside one tenant (D114).</summary>
public sealed class DemoTenant
{
    /// <summary>The id of the tenant.</summary>
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>The ids of the companies of the tenant.</summary>
    [Required]
    [MinLength(1)]
    public List<string> Companies { get; set; } = [];

    /// <summary>The users of the tenant.</summary>
    [Required]
    [ValidateEnumeratedItems]
    public List<DemoUser> Users { get; set; } = [];

    /// <summary>Gives a user of the tenant.</summary>
    /// <param name="id">The id of the user.</param>
    /// <returns>The user, or null when the tenant has no such user.</returns>
    public DemoUser? FindUser(string? id) => Users.Find(user => user.Id == id);
}

/// <summary>One user of a tenant, with an account in each of its companies.</summary>
public sealed class DemoUser
{
    /// <summary>The id of the user in its tenant.</summary>
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>The ids of the companies of the tenant in which the user has an account.</summary>
    [Required]
    [MinLength(1)]
    public List<string> Accounts { get; set; } = [];
}

/// <summary>The source-generated validator of <see cref="DemoDataOptions"/>.</summary>
[OptionsValidator]
public sealed partial class DemoDataOptionsValidator : IValidateOptions<DemoDataOptions>;
