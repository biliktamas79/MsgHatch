using System.ComponentModel.DataAnnotations;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Data.Sqlite;

/// <summary>The settings of the SQLite stores with one database file for each tenant (section 4.4, D125).</summary>
public sealed class SqliteTenantStoreOptions : IValidatableObject
{
    /// <summary>
    /// The key of the section under the root of <c>AddMsgHatch</c> (D329): <c>{root}:SqliteTenants</c>, by default
    /// <c>MsgHatch:SqliteTenants</c>.
    /// </summary>
    public const string SectionKey = "SqliteTenants";

    private const string _tenant = "{tenant}";

    /// <summary>
    /// The connection string of the database of each tenant, for example <c>Data Source=data/msghatch-{tenant}.db</c>.
    /// It must contain <c>{tenant}</c>, which the stores replace with the tenant id, escaped as in the file stores
    /// (D172). The database files must be on a local disk, in a folder that exists.
    /// </summary>
    [Required]
    public string TenantConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The connection string of the system database, for the key <see cref="TenantKey.None"/>: the messages with no
    /// tenant, the down states of the channels, and the registry settings, for example <c>Data Source=data/msghatch.db</c>.
    /// It must not contain <c>{tenant}</c>.
    /// </summary>
    [Required]
    public string SystemConnectionString { get; set; } = string.Empty;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(TenantConnectionString) && !TenantConnectionString.Contains(_tenant, StringComparison.Ordinal))
        {
            yield return new ValidationResult("TenantConnectionString has no {tenant}.", [nameof(TenantConnectionString)]);
        }

        if (SystemConnectionString?.Contains(_tenant, StringComparison.Ordinal) == true)
        {
            yield return new ValidationResult("SystemConnectionString contains {tenant}, but it names the one system database.", [nameof(SystemConnectionString)]);
        }
    }

    /// <summary>
    /// The connection string of the database of a key: the database of its tenant, with the tenant id escaped as a file
    /// name (D172), or the system database.
    /// </summary>
    internal string ConnectionStringOf(TenantKey key) =>
        key.Tenant is { } tenant
            ? TenantConnectionString.Replace(_tenant, FileNames.Escape(tenant.Value), StringComparison.Ordinal)
            : SystemConnectionString;
}
