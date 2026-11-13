using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Data.EntityFrameworkCore;

/// <summary>The settings of the EF Core stores (section 4.4, D120).</summary>
public sealed class EntityFrameworkStoreOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch:EntityFramework";

    /// <summary>The default schema of the tables.</summary>
    public const string DefaultSchema = "msghatch";

    /// <summary>
    /// The database schema of the tables of the stores, for example <c>msghatch</c>. It starts with an ASCII letter or
    /// <c>_</c>, and contains only ASCII letters, digits, and <c>_</c>.
    /// </summary>
    [Required]
    [RegularExpression("^[A-Za-z_][A-Za-z0-9_]*$")]
    public string Schema { get; set; } = DefaultSchema;
}
