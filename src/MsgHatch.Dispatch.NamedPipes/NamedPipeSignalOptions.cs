using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Dispatch.NamedPipes;

/// <summary>The settings of the named pipe dispatch signal (D188, D192).</summary>
public sealed class NamedPipeSignalOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch:NamedPipeSignal";

    /// <summary>
    /// The start of the name of each pipe: <c>{PipeNamePrefix}.inbox</c>, and <c>{PipeNamePrefix}.outbox.{Channel}</c>.
    /// All processes of one system use the same prefix, and two systems on one machine use two prefixes. It has 1 to 40
    /// characters from the ASCII letters, the digits, <c>_</c>, and <c>-</c>.
    /// </summary>
    [Required]
    [RegularExpression("^[A-Za-z0-9_-]{1,40}$")]
    public string PipeNamePrefix { get; set; } = string.Empty;
}
