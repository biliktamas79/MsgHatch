using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Subscriptions.Caching;

/// <summary>The settings of the subscription cache (section 4.4, D38).</summary>
public sealed class SubscriptionCacheOptions
{
    /// <summary>
    /// The key of the section under the root of <c>AddMsgHatch</c> (D329): <c>{root}:SubscriptionCache</c>, by default
    /// <c>MsgHatch:SubscriptionCache</c>.
    /// </summary>
    public const string SectionKey = "SubscriptionCache";

    /// <summary>The time that the cache keeps the subscriptions of one owner and message type.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The source-generated validator checks this rule with its own copy of the attribute, with no reflection.")]
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The start of each cache key and tag, so that the entries do not collide with other entries of a shared cache.</summary>
    [Required]
    [RegularExpression("^[A-Za-z0-9:_-]+$")]
    public string KeyPrefix { get; set; } = "msghatch:sub";
}
