using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Subscriptions.Caching;

/// <summary>The settings of the subscription cache (section 4.4, D38).</summary>
public sealed class SubscriptionCacheOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch:SubscriptionCache";

    /// <summary>The time that the cache keeps the subscriptions of one owner and message type.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The start of each cache key and tag, so that the entries do not collide with other entries of a shared cache.</summary>
    [Required]
    [RegularExpression("^[A-Za-z0-9:_-]+$")]
    public string KeyPrefix { get; set; } = "msghatch:sub";
}
