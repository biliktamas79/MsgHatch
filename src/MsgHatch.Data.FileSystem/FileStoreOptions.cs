using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Data.FileSystem;

/// <summary>The settings of the JSON file stores (section 4.4, D26).</summary>
public sealed class FileStoreOptions
{
    /// <summary>
    /// The key of the section under the root of <c>AddMsgHatch</c> (D329): <c>{root}:FileStores</c>, by default
    /// <c>MsgHatch:FileStores</c>.
    /// </summary>
    public const string SectionKey = "FileStores";

    /// <summary>The folder of the stores. The inbox is under <c>{RootPath}/inbox</c>, and the outbox under <c>{RootPath}/outbox</c>.</summary>
    [Required]
    public string RootPath { get; set; } = string.Empty;
}
