using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Api.Client;

/// <summary>The settings of the HTTP client of the delivery API (D294, section 4.23).</summary>
public sealed class DeliveryApiClientOptions : IValidatableObject
{
    /// <summary>
    /// The key of the section under the root of <c>AddMsgHatch</c> (D329): <c>{root}:DeliveryApiClient</c>, by default
    /// <c>MsgHatch:DeliveryApiClient</c>.
    /// </summary>
    public const string SectionKey = "DeliveryApiClient";

    /// <summary>The absolute URI of the host of the API, for example <c>https://api.example.com/</c>. It has no default.</summary>
    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// The prefix of <c>MapMsgHatchDelivery</c> and <c>MapMsgHatchDeliveryContent</c> on the host, which maps both route groups with
    /// this prefix. The default is <c>/delivery</c>, the default of both methods.
    /// </summary>
    [Required]
    public string Prefix { get; set; } = "/delivery";

    /// <summary>The time limit of one request, from 1 second to 10 minutes. The default is 30 seconds. A request that is too slow gives <c>Errors.Unavailable</c>.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The source-generated validator checks this rule with its own copy of the attribute, with no reflection.")]
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Checks that <see cref="BaseAddress"/> is an absolute URI.</summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>The failures.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseAddress is { IsAbsoluteUri: false })
        {
            yield return new ValidationResult("BaseAddress is an absolute URI.", [nameof(BaseAddress)]);
        }
    }
}
