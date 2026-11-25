using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace SchemaMigrator;

/// <summary>The settings of the schema migrator, from the environment variables of the pipeline (D320).</summary>
public sealed class SchemaMigratorOptions
{
    /// <summary>The configuration section. The environment variable of a key has <c>__</c> in place of <c>:</c>.</summary>
    public const string SectionName = "SchemaMigrator";

    /// <summary>
    /// The connection string with the rights to change the schema, from the environment variable
    /// <c>SchemaMigrator__ConnectionString</c>. The migrator never writes it to the output.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The database of the MsgHatch packages, <see cref="SqlServer"/>, <see cref="PostgreSql"/>, or <see cref="MySql"/>, from
    /// the environment variable <c>SchemaMigrator__Provider</c>.
    /// </summary>
    [Required]
    [AllowedValues(SqlServer, PostgreSql, MySql)]
    public string Provider { get; set; } = SqlServer;

    /// <summary>The SQL Server packages of the EF Core stores.</summary>
    public const string SqlServer = "SqlServer";

    /// <summary>The PostgreSQL packages of the EF Core stores.</summary>
    public const string PostgreSql = "PostgreSql";

    /// <summary>The MySQL packages of the EF Core stores.</summary>
    public const string MySql = "MySql";
}

/// <summary>The source-generated validator of <see cref="SchemaMigratorOptions"/>.</summary>
[OptionsValidator]
public sealed partial class SchemaMigratorOptionsValidator : IValidateOptions<SchemaMigratorOptions>;
