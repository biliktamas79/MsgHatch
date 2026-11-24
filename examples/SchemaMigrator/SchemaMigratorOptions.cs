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
}

/// <summary>The source-generated validator of <see cref="SchemaMigratorOptions"/>.</summary>
[OptionsValidator]
public sealed partial class SchemaMigratorOptionsValidator : IValidateOptions<SchemaMigratorOptions>;
