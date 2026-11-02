using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The core settings of MsgHatch.</summary>
public sealed class MsgHatchOptions : IValidatableObject
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch";

    /// <summary>The language when no other rule gives one. The culture must be known to <see cref="CultureInfo"/>.</summary>
    [Required]
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>Adds or removes all logging wrappers (D35).</summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>The roles of the host (D105). The default is both roles, for a host with one process. It is not <see cref="MsgHatchRoles.None"/>.</summary>
    public MsgHatchRoles Roles { get; set; } = MsgHatchRoles.Consumer | MsgHatchRoles.Publisher;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(DefaultLanguage) && !IsKnownCulture(DefaultLanguage))
        {
            yield return new ValidationResult("DefaultLanguage is not a known culture.", [nameof(DefaultLanguage)]);
        }

        if (Roles == MsgHatchRoles.None)
        {
            yield return new ValidationResult("Roles has at least one role.", [nameof(Roles)]);
        }
    }

    private static bool IsKnownCulture(string name)
    {
        try
        {
            _ = CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
