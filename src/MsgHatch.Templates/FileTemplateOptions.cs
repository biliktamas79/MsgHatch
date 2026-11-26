using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Templates;

/// <summary>The settings of the shared file template store (section 4.4).</summary>
public sealed class FileTemplateOptions
{
    /// <summary>
    /// The key of the section under the root of <c>AddMsgHatch</c> (D329): <c>{root}:FileTemplates</c>, by default
    /// <c>MsgHatch:FileTemplates</c>.
    /// </summary>
    public const string SectionKey = "FileTemplates";

    /// <summary>The folder of the template files.</summary>
    [Required]
    public string RootPath { get; set; } = string.Empty;
}
