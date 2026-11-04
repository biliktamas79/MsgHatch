using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Data.Sqlite;

/// <summary>The settings of the SQLite stores (section 4.4, D55).</summary>
public sealed class SqliteStoreOptions : IValidatableObject
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch:Sqlite";

    /// <summary>
    /// The connection string of the one database of the stores, for example <c>Data Source=msghatch.db</c>. The database
    /// file must be on a local disk. It must not contain <c>{tenant}</c>, because one database for each tenant has its own
    /// setup method (D125).
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ConnectionString?.Contains("{tenant}", StringComparison.Ordinal) == true)
        {
            yield return new ValidationResult(
                "ConnectionString contains {tenant}, but one database for each tenant has its own setup method.", [nameof(ConnectionString)]);
        }
    }
}
