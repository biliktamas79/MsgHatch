using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Domain.Dispatch;

/// <summary>The automatic down state of one channel (D86). The name of the options is the channel name.</summary>
public sealed class ChannelDownOptions
{
    /// <summary>The configuration section. The section of a channel is <c>MsgHatch:ChannelDown:{Channel}</c>.</summary>
    public const string SectionName = "MsgHatch:ChannelDown";

    /// <summary>The number of transient errors in a row after which a dispatch loop marks the channel down.</summary>
    [Range(1, 1000)]
    public int ErrorThreshold { get; set; } = 5;

    /// <summary>The time between two health checks of a down channel.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The source-generated validator checks this rule with its own copy of the attribute, with no reflection.")]
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}
