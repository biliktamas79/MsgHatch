using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Options;
using MsgHatch.Broker.AmazonSns.Tests.Unit.TestTypes;
using MsgHatch.Broker.Tests.Unit.Contracts;
using MsgHatch.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Broker.AmazonSns.Tests.Unit;

/// <summary>
/// The Amazon SNS broker client: the use cases of the broker client contract, and the rules that only SNS has: the topic
/// of the content type, the request, and the limits of the message attributes (D300, D301).
/// </summary>
public sealed class AmazonSnsBrokerClientUnitTests : BrokerClientTestsBase<AmazonSnsBrokerClient>
{
    private const string _topicOfTheTestMessage = "arn:aws:sns:eu-west-1:123456789012:test-message";
    private const string _defaultTopic = "arn:aws:sns:eu-west-1:123456789012:default";

    private readonly SnsTestDriver _driver;
    private readonly IAmazonSimpleNotificationService _sns;

    public AmazonSnsBrokerClientUnitTests()
        : this(new SnsTestDriver())
    {
    }

    private AmazonSnsBrokerClientUnitTests(SnsTestDriver driver)
        : base(new AmazonSnsBrokerClient(driver.Sns, Options.Create(AnOptions())), driver)
    {
        _driver = driver;
        _sns = driver.Sns;
    }

    public static TheoryData<string, string> InvalidHeaderNameRows => new()
    {
        { "an empty name", "" },
        { "a space", "x source" },
        { "a colon", "x:source" },
        { "a letter outside ASCII", "x-forrás" },
        { "a period at the start", ".x-source" },
        { "a period at the end", "x-source." },
        { "two periods in a row", "x..source" },
        { "257 characters", new string('a', 257) },
        { "the prefix AWS.", "AWS.source" },
        { "the prefix aws.", "aws.source" },
        { "the prefix Amazon.", "Amazon.source" },
        { "the prefix AMAZON.", "AMAZON.source" },
    };

    public static TheoryData<string, string> ValidHeaderNameRows => new()
    {
        { "letters, digits, an underscore, a hyphen, and a period", "x_Source-2.v1" },
        { "256 characters", new string('a', 256) },
        { "AWS with no period after it", "AWS-source" },
        { "Amazon with no period after it", "Amazonsource" },
    };

    public static TheoryData<string, string> InvalidHeaderValueRows => new()
    {
        { "an empty value", "" },
        { "a surrogate pair", "order \U0001F600" },
    };

    [Fact]
    public async Task PublishAsync_ContentTypeWithATopic_PublishesToThatTopic()
    {
        // Arrange
        Driver.Accepts();

        // Act
        await Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal(_topicOfTheTestMessage, _driver.SentRequest().TopicArn);
    }

    [Fact]
    public async Task PublishAsync_ContentTypeWithNoTopic_PublishesToTheDefaultTopic()
    {
        // Arrange
        Driver.Accepts();

        // Act
        await Client.PublishAsync(APublish() with { ContentType = "Broker:OtherMessage" }, Ct);

        // Assert
        Assert.Equal(_defaultTopic, _driver.SentRequest().TopicArn);
    }

    [Fact]
    public async Task PublishAsync_ContentTypeWithNoTopicAndNoDefaultTopic_ReturnsNotFoundAndCallsNoSns()
    {
        // Arrange
        var options = new AmazonSnsOptions { Topics = { ["TestMessage"] = _topicOfTheTestMessage } };
        var client = new AmazonSnsBrokerClient(_sns, Options.Create(options));

        // Act
        var result = await client.PublishAsync(APublish() with { ContentType = "Broker:OtherMessage" }, Ct);

        // Assert
        Assert.Equal(Errors.NotFound, result.Error?.Type);
        Assert.Empty(_sns.ReceivedCalls());
    }

    [Fact]
    public async Task PublishAsync_Payload_SendsThePayloadJsonAsTheMessage()
    {
        // Arrange
        Driver.Accepts();

        // Act
        await Client.PublishAsync(APublish(payload: new TestPayload("order 1001")), Ct);

        // Assert
        Assert.Equal("""{"text":"order 1001"}""", _driver.SentRequest().Message);
    }

    [Fact]
    public async Task PublishAsync_Headers_SendsEachHeaderAsAStringMessageAttribute()
    {
        // Arrange
        Driver.Accepts();
        var headers = new Dictionary<string, string> { ["msghatch-id"] = "id-1", ["x-source"] = "orders" };

        // Act
        await Client.PublishAsync(APublish(headers: headers), Ct);

        // Assert
        Assert.Equal(
            [("msghatch-id", "String", "id-1"), ("x-source", "String", "orders")],
            _driver.SentRequest().MessageAttributes
                .Select(static attribute => (attribute.Key, attribute.Value.DataType, attribute.Value.StringValue))
                .OrderBy(static attribute => attribute.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_10Headers_PublishesTheMessage()
    {
        // Arrange
        var receipt = Driver.Accepts();

        // Act
        var result = await Client.PublishAsync(APublish(headers: HeadersOf(count: 10)), Ct);

        // Assert
        Assert.Equal(receipt, result.Value);
    }

    [Fact]
    public async Task PublishAsync_11Headers_ReturnsValidationFailedAndCallsNoSns()
    {
        // Act
        var result = await Client.PublishAsync(APublish(headers: HeadersOf(count: 11)), Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
        Assert.Empty(_sns.ReceivedCalls());
    }

    [Theory]
    [MemberData(nameof(InvalidHeaderNameRows))]
    public async Task PublishAsync_HeaderNameThatSnsDoesNotAccept_ReturnsValidationFailedAndCallsNoSns(string row, string name)
    {
        // Act
        var result = await Client.PublishAsync(APublish(headers: new Dictionary<string, string> { [name] = "value" }), Ct);

        // Assert
        Assert.Equal((row, Errors.ValidationFailed, 0), (row, result.Error?.Type, _sns.ReceivedCalls().Count()));
    }

    [Theory]
    [MemberData(nameof(ValidHeaderNameRows))]
    public async Task PublishAsync_HeaderNameThatSnsAccepts_PublishesTheMessage(string row, string name)
    {
        // Arrange
        Driver.Accepts();

        // Act
        var result = await Client.PublishAsync(APublish(headers: new Dictionary<string, string> { [name] = "value" }), Ct);

        // Assert
        Assert.Equal((row, (Error?)null), (row, result.IsError ? result.Error : null));
    }

    [Theory]
    [MemberData(nameof(InvalidHeaderValueRows))]
    public async Task PublishAsync_HeaderValueThatSnsDoesNotAccept_ReturnsValidationFailedWithNoValueAndCallsNoSns(string row, string value)
    {
        // Act
        var result = await Client.PublishAsync(APublish(headers: new Dictionary<string, string> { ["x-source"] = value }), Ct);

        // Assert
        Assert.Equal((row, Errors.ValidationFailed, 0), (row, result.Error?.Type, _sns.ReceivedCalls().Count()));
        Assert.DoesNotContain("order", result.Error?.Message ?? "order", StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_HeaderValueWithOneSurrogate_ReturnsValidationFailedAndCallsNoSns()
    {
        // Arrange
        var value = "order " + (char)0xD83D;

        // Act
        var result = await Client.PublishAsync(APublish(headers: new Dictionary<string, string> { ["x-source"] = value }), Ct);

        // Assert
        Assert.Equal((Errors.ValidationFailed, 0), (result.Error?.Type, _sns.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task PublishAsync_SnsExceptionWithAHeaderValue_ReturnsAnErrorMessageWithNoHeaderValue()
    {
        // Arrange
        Driver.Fails(Errors.Rejected);

        // Act
        var result = await Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.DoesNotContain("x-secret", result.Error?.Message ?? "x-secret", StringComparison.Ordinal);
    }

    private static AmazonSnsOptions AnOptions() => new()
    {
        DefaultTopicArn = _defaultTopic,
        Topics = { ["TestMessage"] = _topicOfTheTestMessage },
    };

    private static Dictionary<string, string> HeadersOf(int count) =>
        Enumerable.Range(1, count).ToDictionary(static i => $"x-header-{i}", static i => $"value-{i}", StringComparer.Ordinal);
}
