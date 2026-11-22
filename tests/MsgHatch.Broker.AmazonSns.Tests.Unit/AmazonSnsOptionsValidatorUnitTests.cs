using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Broker.AmazonSns.Tests.Unit;

/// <summary>The rules of the SNS options: the region, each topic ARN, a standard topic, and at least one topic (D300, D301).</summary>
public sealed class AmazonSnsOptionsValidatorUnitTests
{
    private const string _topic = "arn:aws:sns:eu-west-1:123456789012:orders";
    private const string _region = nameof(AmazonSnsOptions.Region);
    private const string _defaultTopicArn = nameof(AmazonSnsOptions.DefaultTopicArn);
    private const string _topics = nameof(AmazonSnsOptions.Topics);

    public static TheoryData<string?, string[]> RegionRows => new()
    {
        { null, [] },
        { "eu-west-1", [] },
        { "EU-WEST-1", [_region] },
    };

    public static TheoryData<string, string, bool> TopicArnRows => new()
    {
        { "a standard topic", _topic, true },
        { "a topic of the partition aws-cn", "arn:aws-cn:sns:cn-north-1:123456789012:orders", true },
        { "a topic of the partition aws-us-gov", "arn:aws-us-gov:sns:us-gov-west-1:123456789012:orders", true },
        { "a topic name with digits, a hyphen, and an underscore", "arn:aws:sns:eu-west-1:123456789012:orders_2-a", true },
        { "a topic name of 256 characters", $"arn:aws:sns:eu-west-1:123456789012:{new string('a', 256)}", true },
        { "a topic name of 257 characters", $"arn:aws:sns:eu-west-1:123456789012:{new string('a', 257)}", false },
        { "a FIFO topic", "arn:aws:sns:eu-west-1:123456789012:orders.fifo", false },
        { "an SQS queue", "arn:aws:sqs:eu-west-1:123456789012:orders", false },
        { "an account of 11 digits", "arn:aws:sns:eu-west-1:12345678901:orders", false },
        { "no region", "arn:aws:sns::123456789012:orders", false },
        { "a topic name only", "orders", false },
        { "an empty value", "", false },
    };

    [Theory]
    [MemberData(nameof(RegionRows))]
    public void Validate_Region_FailsWhenItIsNotTheNameOfARegion(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new AmazonSnsOptions { Region = value, DefaultTopicArn = _topic };

        // Act
        var result = new AmazonSnsOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(TopicArnRows))]
    public void Validate_DefaultTopicArn_FailsWhenItIsNotTheArnOfAStandardTopic(string row, string value, bool valid)
    {
        // Arrange
        var options = new AmazonSnsOptions { DefaultTopicArn = value };

        // Act
        var result = new AmazonSnsOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal((row, valid ? "" : _defaultTopicArn), (row, string.Join(' ', OptionsFailures.FailedProperties(result))));
    }

    [Theory]
    [MemberData(nameof(TopicArnRows))]
    public void Validate_TopicOfARegistration_FailsWhenItIsNotTheArnOfAStandardTopic(string row, string value, bool valid)
    {
        // Arrange
        var options = new AmazonSnsOptions { Topics = { ["DEvent"] = _topic, ["EEvent"] = value } };

        // Act
        var result = new AmazonSnsOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal((row, valid ? "" : _topics), (row, string.Join(' ', OptionsFailures.FailedProperties(result))));
    }

    [Fact]
    public void Validate_NoDefaultTopicAndNoTopicOfARegistration_FailsForTheTopics()
    {
        // Arrange
        var options = new AmazonSnsOptions { Region = "eu-west-1" };

        // Act
        var result = new AmazonSnsOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal([_topics], OptionsFailures.FailedProperties(result));
    }

    [Fact]
    public void Validate_OnlyATopicOfARegistration_Succeeds()
    {
        // Arrange
        var options = new AmazonSnsOptions { Topics = { ["DEvent"] = _topic } };

        // Act
        var result = new AmazonSnsOptionsValidator().Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }
}
