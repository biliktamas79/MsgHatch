using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Data.EFCore;

/// <summary>The settings of the EF Core stores (section 4.4, D120, D318).</summary>
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

    /// <summary>
    /// The collation of each text column, so of each id and name, for example <c>Latin1_General_100_BIN2</c>. With no
    /// value, the collation comes from the dialect of the provider package, or else from the database. It contains only
    /// ASCII letters, digits, <c>_</c>, <c>.</c>, and <c>-</c>.
    /// </summary>
    [MinLength(1)]
    [RegularExpression("^[A-Za-z0-9_.-]+$")]
    public string? TextCollation { get; set; }

    /// <summary>
    /// Whether the start of the host fails when a migration of a MsgHatch context is not applied (D320). The check reads
    /// only the history tables of the migrations, so it needs no right to change the schema.
    /// </summary>
    public bool RequireCurrentSchema { get; set; }
}
