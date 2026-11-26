using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace MsgHatch.Channels.Broker.AmazonSns;

/// <summary>The settings of the Amazon SNS broker client (section 4.4, D300, D301).</summary>
public sealed partial class AmazonSnsOptions : IValidatableObject
{
    /// <summary>
    /// The key of the section under the root of <c>AddMsgHatch</c> (D329): <c>{root}:AmazonSns</c>, by default
    /// <c>MsgHatch:AmazonSns</c>.
    /// </summary>
    public const string SectionKey = "AmazonSns";

    /// <summary>The AWS region of SNS, for example <c>eu-west-1</c>, or null for the region of the AWS SDK configuration.</summary>
    [RegularExpression("^[a-z]{2}(-[a-z]+)+-[0-9]$")]
    public string? Region { get; set; }

    /// <summary>
    /// The ARN of the topic of each broker registration with no topic in <see cref="Topics"/>, or null for none. It must
    /// be a standard topic. A FIFO topic, an ARN that ends with <c>.fifo</c>, is not valid (D301).
    /// </summary>
    public string? DefaultTopicArn { get; set; }

    /// <summary>
    /// The topic ARN of each broker registration, with the name of the registration as the key: <c>DEvent</c> for the
    /// content type <c>Broker:DEvent</c>. The key is not the content type, because a colon in a configuration key
    /// separates two sections. Each ARN must be a standard topic.
    /// </summary>
    public Dictionary<string, string> Topics { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DefaultTopicArn is null && Topics.Count == 0)
        {
            yield return new ValidationResult(
                $"no topic. Set {nameof(DefaultTopicArn)}, or a topic for at least one broker registration.", [nameof(Topics)]);
        }

        if (DefaultTopicArn is not null && !StandardTopicArn().IsMatch(DefaultTopicArn))
        {
            yield return new ValidationResult("the value is not the ARN of a standard SNS topic.", [nameof(DefaultTopicArn)]);
        }

        foreach (var (name, arn) in Topics)
        {
            if (!StandardTopicArn().IsMatch(arn ?? string.Empty))
            {
                yield return new ValidationResult($"the topic of {name} is not the ARN of a standard SNS topic.", [nameof(Topics)]);
            }
        }
    }

    [GeneratedRegex("^arn:aws(-[a-z]+)*:sns:[a-z]{2}(-[a-z]+)+-[0-9]:[0-9]{12}:[A-Za-z0-9_-]{1,256}$")]
    private static partial Regex StandardTopicArn();
}
