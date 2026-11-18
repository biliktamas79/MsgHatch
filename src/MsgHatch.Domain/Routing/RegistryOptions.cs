using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Domain.Routing;

/// <summary>The settings of the stored registry (D118, section 4.20).</summary>
public sealed class RegistryOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch:Registry";

    /// <summary>The time between two reads of the registry settings store. A write of another process is visible after it.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The source-generated validator checks this rule with its own copy of the attribute, with no reflection.")]
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}
