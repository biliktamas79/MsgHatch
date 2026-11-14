using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace WolverineExample.UserSimulator;

/// <summary>The settings of the user simulator, from the section <c>UserSimulator</c>.</summary>
public sealed class UserSimulatorOptions
{
    /// <summary>The configuration section of the options.</summary>
    public const string SectionName = "UserSimulator";

    /// <summary>The URL of the SignalR hub of the consumer.</summary>
    [Required]
    public Uri? HubUrl { get; set; }

    /// <summary>The issuer of the tokens, which the consumer accepts.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The audience of the tokens, which the consumer accepts.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The simulated users. Each one gets its own SignalR connection.</summary>
    [Required]
    [MinLength(1)]
    [ValidateEnumeratedItems]
    public List<SimulatedUser> Users { get; set; } = [];
}

/// <summary>One simulated user of the consumer's directory.</summary>
public sealed class SimulatedUser
{
    /// <summary>The tenant of the user.</summary>
    [Required]
    public string Tenant { get; set; } = string.Empty;

    /// <summary>The id of the user in its tenant.</summary>
    [Range(1, int.MaxValue)]
    public int UserId { get; set; }
}

/// <summary>
/// The signing keys that <c>dotnet user-jwts</c> writes into the user secrets of the consumer, from the section
/// <c>Authentication:Schemes:Bearer</c>. The JWT bearer handler of the consumer reads the same section.
/// </summary>
public sealed class UserJwtsOptions
{
    /// <summary>The configuration section of the options.</summary>
    public const string SectionName = "Authentication:Schemes:Bearer";

    /// <summary>The signing keys.</summary>
    [MinLength(1, ErrorMessage = "No signing key of dotnet user-jwts. Run: dotnet user-jwts create --project examples/WolverineExample.Consumer")]
    [ValidateEnumeratedItems]
    public List<UserJwtsSigningKey> SigningKeys { get; set; } = [];
}

/// <summary>One signing key of <c>dotnet user-jwts</c>.</summary>
public sealed class UserJwtsSigningKey
{
    /// <summary>The issuer of the tokens of the key.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The key, as Base64.</summary>
    [Required]
    public string Value { get; set; } = string.Empty;
}

/// <summary>The source-generated validator of <see cref="UserSimulatorOptions"/>.</summary>
[OptionsValidator]
public sealed partial class UserSimulatorOptionsValidator : IValidateOptions<UserSimulatorOptions>;

/// <summary>The source-generated validator of <see cref="UserJwtsOptions"/>.</summary>
[OptionsValidator]
public sealed partial class UserJwtsOptionsValidator : IValidateOptions<UserJwtsOptions>;
