using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Email.Templates;

/// <summary>The settings of the shared file template store (section 4.4).</summary>
public sealed class FileTemplateOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch:FileTemplates";

    /// <summary>The folder of the template files.</summary>
    [Required]
    public string RootPath { get; set; } = string.Empty;
}
