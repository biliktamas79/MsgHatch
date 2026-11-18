using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace AdminUiExample;

/// <summary>The settings of the admin UI example, from the section <c>AdminUiExample</c>.</summary>
public sealed class AdminUiExampleOptions
{
    /// <summary>The configuration section of the options.</summary>
    public const string SectionName = "AdminUiExample";

    /// <summary>
    /// The token that the clients send to the APIs of <c>ExampleApp</c>, for example from
    /// <c>dotnet user-jwts create --project examples/ExampleApp --name 1 --role TemplateAdmin</c>. Keep it in the user secrets
    /// of this project, which git ignores, and never in a file of the repository.
    /// </summary>
    [Required]
    public string ApiToken { get; set; } = string.Empty;
}

/// <summary>The source-generated validator of <see cref="AdminUiExampleOptions"/>.</summary>
[OptionsValidator]
public sealed partial class AdminUiExampleOptionsValidator : IValidateOptions<AdminUiExampleOptions>;
