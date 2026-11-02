using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Domain.Delivery;

/// <summary>The test delivery of one channel (D83, section 4.8). The name of the options is the channel name.</summary>
public sealed class TestDeliveryOptions : IValidatableObject
{
    /// <summary>The configuration section. The section of a channel is <c>MsgHatch:TestDelivery:{Channel}</c>.</summary>
    public const string SectionName = "MsgHatch:TestDelivery";

    /// <summary>How the channel handles a test message.</summary>
    [EnumDataType(typeof(TestDeliveryMode))]
    public TestDeliveryMode Mode { get; set; } = TestDeliveryMode.Suppress;

    /// <summary>The test address or the test group of <see cref="TestDeliveryMode.Redirect"/>. It is required for that mode.</summary>
    [MaxLength(256)]
    public string? RedirectTo { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Mode == TestDeliveryMode.Redirect && string.IsNullOrWhiteSpace(RedirectTo))
        {
            yield return new ValidationResult("RedirectTo is required for the mode Redirect.", [nameof(RedirectTo)]);
        }
    }
}
