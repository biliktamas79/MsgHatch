using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Domain.Delivery;

/// <summary>
/// The retry settings of the inbox, of a channel, or of one channel registration (D87). The name of the options is
/// <c>Inbox</c>, the channel name, or the content type of the channel registration.
/// </summary>
public sealed class RetryOptions : IValidatableObject
{
    /// <summary>The configuration section. The section of a name is <c>MsgHatch:Retry:{Name}</c>.</summary>
    public const string SectionName = "MsgHatch:Retry";

    /// <summary>The maximum number of tries of one message.</summary>
    [Range(1, 100)]
    public int MaxTryCount { get; set; } = 8;

    /// <summary>The delay after the first failed try.</summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "01:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The longest delay between two tries. It is not less than <see cref="BaseDelay"/>.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "7.00:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>The time after which a message is not sent, or null for no expiry (D94).</summary>
    [Range(typeof(TimeSpan), "00:00:01", "30.00:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan? Expiry { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MaxDelay < BaseDelay)
        {
            yield return new ValidationResult("MaxDelay is not less than BaseDelay.", [nameof(MaxDelay)]);
        }
    }
}
