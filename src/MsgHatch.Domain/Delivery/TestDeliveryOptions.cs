using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Domain.Delivery;

/// <summary>The test delivery of one channel (D83, section 4.8). The name of the options is the channel name.</summary>
public sealed class TestDeliveryOptions : IValidatableObject
{
    /// <summary>
    /// The key of the section under the root of <c>AddMsgHatch</c> (D329). The section of a channel is
    /// <c>{root}:TestDelivery:{Channel}</c>, by default <c>MsgHatch:TestDelivery:{Channel}</c>.
    /// </summary>
    public const string SectionKey = "TestDelivery";

    /// <summary>How the channel handles a test message.</summary>
    [EnumDataType(typeof(TestDeliveryMode))]
    public TestDeliveryMode Mode { get; set; } = TestDeliveryMode.Suppress;

    /// <summary>The test address or the test group of <see cref="TestDeliveryMode.Redirect"/>. It is required for that mode.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The source-generated validator checks this rule with its own copy of the attribute, with no reflection.")]
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
