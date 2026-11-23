using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace SignalRDemo.Tenants;

/// <summary>The tenants, the companies, and the users of the demo, from the section <c>Demo</c> of <c>demo-data.json</c>.</summary>
public sealed class DemoDataOptions
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
