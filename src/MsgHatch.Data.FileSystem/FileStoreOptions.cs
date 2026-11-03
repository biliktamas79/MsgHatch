using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Data.FileSystem;

/// <summary>The settings of the JSON file stores (section 4.4, D26).</summary>
public sealed class FileStoreOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch:FileStores";

    /// <summary>The folder of the stores. The inbox is under <c>{RootPath}/inbox</c>, and the outbox under <c>{RootPath}/outbox</c>.</summary>
    [Required]
    public string RootPath { get; set; } = string.Empty;
}
