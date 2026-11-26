using System.Net;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using MsgHatch.Channels.Broker.AmazonSns.Tests.Unit.TestTypes;
using MsgHatch.Channels.Broker.Tests.Unit.Contracts;
using MsgHatch.Channels.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using AwsErrorType = Amazon.Runtime.ErrorType;
using SnsConflictClassifier = MsgHatch.Domain.Tests.Unit.TestTypes.ConflictClassifier<MsgHatch.Channels.Broker.AmazonSns.AmazonSnsBrokerClient>;

namespace MsgHatch.Channels.Broker.AmazonSns.Tests.Unit;

/// <summary>
/// The Amazon SNS broker client: the use cases of the broker client contract, and the rules that only SNS has: the topic
/// of the content type, the request, the limits of the message attributes, the error of each SNS exception, and the
/// health check of the topics (D300 to D302).
/// </summary>
public sealed class AmazonSnsBrokerClientUnitTests : BrokerClientTestsBase<AmazonSnsBrokerClient>
{
    private const string _topicOfTheTestMessage = "arn:aws:sns:eu-west-1:123456789012:test-message";
    private const string _defaultTopic = "arn:aws:sns:eu-west-1:123456789012:default";

    private readonly SnsTestDriver _driver;
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly FakeLogger<AmazonSnsBrokerClient> _logger;

    public AmazonSnsBrokerClientUnitTests()
        : this(new SnsTestDriver(), new FakeLogger<AmazonSnsBrokerClient>())
    {
    }

    private AmazonSnsBrokerClientUnitTests(SnsTestDriver driver, FakeLogger<AmazonSnsBrokerClient> logger)
        : base(new AmazonSnsBrokerClient(driver.Sns, Options.Create(AnOptions()), logger), driver)
    {
        _driver = driver;
        _sns = driver.Sns;
        _logger = logger;
    }

    public static TheoryData<string, Exception, int> ExceptionRows => new()
    {
        { "a throttled request", new ThrottledException("Rate exceeded."), 429 },
        { "a throttled request of KMS", new KMSThrottlingException("Rate exceeded."), 429 },
        { "the code Throttled with no type of its own", AServiceError("Throttled", 400), 429 },
        { "the code Throttling with no type of its own", AServiceError("Throttling", 400), 429 },
        { "the code KMSThrottling with no type of its own", AServiceError("KMSThrottling", 400), 429 },
        { "an HTTP 429 response", AServiceError("Other", 429), 429 },
        { "an invalid parameter", new InvalidParameterException("Invalid parameter."), 403 },
        { "an invalid parameter value", new InvalidParameterValueException("Invalid parameter value."), 403 },
        { "a topic that does not exist", new NotFoundException("Topic does not exist."), 503 },
        { "no permission", new AuthorizationErrorException("Not authorized."), 503 },
        { "no access to the KMS key", new KMSAccessDeniedException("Access denied."), 503 },
        { "a disabled KMS key", new KMSDisabledException("Disabled."), 503 },
        { "a KMS key in an invalid state", new KMSInvalidStateException("Invalid state."), 503 },
        { "a KMS key that does not exist", new KMSNotFoundException("Not found."), 503 },
        { "a KMS subscription that is missing", new KMSOptInRequiredException("Opt-in required."), 503 },
        { "no network", new HttpRequestException("No connection."), 503 },
        { "an internal error of SNS", new InternalErrorException("Internal error."), 503 },
        { "an HTTP 499 response", AnHttpError(499), 500 },
        { "an HTTP 500 response", AnHttpError(500), 503 },
        { "an HTTP 501 response", AnHttpError(501), 503 },
        { "another SNS exception", new InvalidSecurityException("Invalid security."), 500 },
        { "another exception", new InvalidOperationException("Defect."), 500 },
    };

    public static TheoryData<string, string?, string?, double?> WaitHeaderRows => new()
    {
        { "x-amz-retry-after in milliseconds", "2500", null, 2.5 },
        { "Retry-After in seconds", null, "3", 3 },
        { "both headers: x-amz-retry-after", "2500", "3", 2.5 },
        { "no header: no wait", null, null, null },
        { "headers that are not a count: no wait", "soon", "Wed, 21 Oct 2026 07:28:00 GMT", null },
        { "a negative count: no wait", "-5", "-3", null },
    };

    public static TheoryData<string, Exception> AdministratorErrorRows => new()
    {
        { "a topic that does not exist", new NotFoundException("Topic does not exist: x-secret.") },
        { "no permission", new AuthorizationErrorException("Not authorized: x-secret.") },
        { "no access to the KMS key", new KMSAccessDeniedException("Access denied: x-secret.") },
        { "a disabled KMS key", new KMSDisabledException("Disabled: x-secret.") },
        { "a KMS key in an invalid state", new KMSInvalidStateException("Invalid state: x-secret.") },
        { "a KMS key that does not exist", new KMSNotFoundException("Not found: x-secret.") },
        { "a KMS subscription that is missing", new KMSOptInRequiredException("Opt-in required: x-secret.") },
    };

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
        var payload = new TestPayload("order 1001");

        // Act
        await Client.PublishAsync(APublish(payload: payload), Ct);

        // Assert
        Assert.Equal($$"""{"text":"{{payload.Text}}"}""", _driver.SentRequest().Message);
    }

    [Fact]
    public async Task PublishAsync_Headers_SendsEachHeaderAsAStringMessageAttribute()
    {
        // Arrange
        const string idHeader = "msghatch-id";
        const string id = "id-1";
        const string sourceHeader = "x-source";
        const string source = "orders";
        Driver.Accepts();
        var headers = new Dictionary<string, string> { [idHeader] = id, [sourceHeader] = source };

        // Act
        await Client.PublishAsync(APublish(headers: headers), Ct);

        // Assert
        Assert.Equal(
            [(idHeader, "String", id), (sourceHeader, "String", source)],
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
        await _sns.Received(1).PublishAsync(Arg.Is<PublishRequest>(static r => r.MessageAttributes.Count == 10), Ct);
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
        await _sns.Received(1).PublishAsync(Arg.Is<PublishRequest>(r => r.MessageAttributes.ContainsKey(name)), Ct);
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
        await _sns.Received(1).PublishAsync(Arg.Is<PublishRequest>(static r => r.TopicArn == _topicOfTheTestMessage), Ct);
    }

    [Theory]
    [MemberData(nameof(ExceptionRows))]
    public async Task PublishAsync_SnsException_ReturnsTheErrorTypeOfTheException(string row, Exception exception, int expectedCode)
    {
        // Arrange
        _sns.PublishAsync(default(PublishRequest)!, Ct).ThrowsAsyncForAnyArgs(exception);

        // Act
        var result = await Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal((row, (int?)expectedCode), (row, result.Error?.Type.Code));
        await _sns.Received(1).PublishAsync(Arg.Is<PublishRequest>(static r => r.TopicArn == _topicOfTheTestMessage), Ct);
    }

    [Fact]
    public async Task PublishAsync_ClassifierKnowsTheException_ReturnsItsErrorTypeBeforeTheBuiltInMapping()
    {
        // Arrange
        var client = new AmazonSnsBrokerClient(_sns, Options.Create(AnOptions()), classifiers: [new SnsConflictClassifier()]);
        _sns.PublishAsync(default(PublishRequest)!, Ct).ThrowsAsyncForAnyArgs(new HttpRequestException());

        // Act
        var result = await client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal(Errors.Conflict, result.Error?.Type);
        await _sns.Received(1).PublishAsync(Arg.Is<PublishRequest>(static r => r.TopicArn == _topicOfTheTestMessage), Ct);
    }

    [Fact]
    public async Task PublishAsync_CancelledTokenAndAClassifier_ReturnsOperationCancelled()
    {
        // Arrange
        var client = new AmazonSnsBrokerClient(_sns, Options.Create(AnOptions()), classifiers: [new SnsConflictClassifier()]);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await stop.CancelAsync();
        _sns.PublishAsync(default(PublishRequest)!, Ct).ThrowsAsyncForAnyArgs(new OperationCanceledException());

        // Act
        var result = await client.PublishAsync(APublish(), stop.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
        await _sns.Received(1).PublishAsync(Arg.Is<PublishRequest>(static r => r.TopicArn == _topicOfTheTestMessage), stop.Token);
    }

    [Theory]
    [MemberData(nameof(AdministratorErrorRows))]
    public async Task PublishAsync_ErrorThatNeedsAnAdministrator_LogsACriticalRecordWithNoTextOfTheException(string row, Exception exception)
    {
        // Arrange
        _sns.PublishAsync(default(PublishRequest)!, Ct).ThrowsAsyncForAnyArgs(exception);

        // Act
        await Client.PublishAsync(APublish(), Ct);

        // Assert
        var record = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal((row, LogLevel.Critical, 2310, false), (row, record.Level, record.Id.Id, record.Message.Contains("x-secret", StringComparison.Ordinal)));
        await _sns.Received(1).PublishAsync(Arg.Is<PublishRequest>(static r => r.TopicArn == _topicOfTheTestMessage), Ct);
    }

    [Fact]
    public async Task PublishAsync_RejectedMessage_LogsNothing()
    {
        // Arrange
        Driver.Fails(Errors.Rejected);

        // Act
        await Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Empty(_logger.Collector.GetSnapshot());
        await _sns.Received(1).PublishAsync(Arg.Is<PublishRequest>(static r => r.TopicArn == _topicOfTheTestMessage), Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_DefaultTopicAndTopicsOfRegistrations_CallsGetTopicAttributesOneTimeForEachTopic()
    {
        // Arrange
        var options = AnOptions();
        options.Topics["OtherMessage"] = _defaultTopic;
        var client = new AmazonSnsBrokerClient(_sns, Options.Create(options));

        // Act
        await client.CheckHealthAsync(Ct);

        // Assert
        Assert.Equal(
            [_defaultTopic, _topicOfTheTestMessage],
            _sns.ReceivedCalls().Select(static call => ((GetTopicAttributesRequest)call.GetArguments()[0]!).TopicArn).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CheckHealthAsync_EachTopicExists_ReturnsNull()
    {
        // Arrange
        _sns.GetTopicAttributesAsync(default(GetTopicAttributesRequest)!, Ct).ReturnsForAnyArgs(Task.FromResult(new GetTopicAttributesResponse()));

        // Act
        var error = await Client.CheckHealthAsync(Ct);

        // Assert
        Assert.Null(error);
        await _sns.Received(1).GetTopicAttributesAsync(Arg.Is<GetTopicAttributesRequest>(static r => r.TopicArn == _defaultTopic), Ct);
        await _sns.Received(1).GetTopicAttributesAsync(Arg.Is<GetTopicAttributesRequest>(static r => r.TopicArn == _topicOfTheTestMessage), Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_TwoTopicsFail_ReturnsTheErrorOfTheFirstTopic()
    {
        // Arrange
        _sns.GetTopicAttributesAsync(Arg.Is<GetTopicAttributesRequest>(static r => r.TopicArn == _defaultTopic), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotFoundException("Topic does not exist."));
        _sns.GetTopicAttributesAsync(Arg.Is<GetTopicAttributesRequest>(static r => r.TopicArn == _topicOfTheTestMessage), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ThrottledException("Rate exceeded."));

        // Act
        var error = await Client.CheckHealthAsync(Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
        await _sns.Received(1).GetTopicAttributesAsync(Arg.Is<GetTopicAttributesRequest>(static r => r.TopicArn == _defaultTopic), Ct);
        await _sns.DidNotReceive().GetTopicAttributesAsync(Arg.Is<GetTopicAttributesRequest>(static r => r.TopicArn == _topicOfTheTestMessage), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckHealthAsync_TopicThatDoesNotExist_LogsACriticalRecord()
    {
        // Arrange
        _sns.GetTopicAttributesAsync(default(GetTopicAttributesRequest)!, Ct).ThrowsAsyncForAnyArgs(new NotFoundException("Topic does not exist."));

        // Act
        await Client.CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal((LogLevel.Critical, 2310), (record.Level, record.Id.Id));
        await _sns.Received(1).GetTopicAttributesAsync(Arg.Is<GetTopicAttributesRequest>(static r => r.TopicArn == _defaultTopic), Ct);
        await _sns.DidNotReceive().GetTopicAttributesAsync(Arg.Is<GetTopicAttributesRequest>(static r => r.TopicArn == _topicOfTheTestMessage), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckHealthAsync_TopicThatDoesNotExist_ReturnsUnavailable()
    {
        // Arrange
        using var sns = FakeSnsHttp.ClientThatAnswers(HttpStatusCode.NotFound, "NotFound", new Dictionary<string, string>());
        var client = new AmazonSnsBrokerClient(sns, Options.Create(AnOptions()));

        // Act
        var error = await client.CheckHealthAsync(Ct);

        // Assert
        Assert.Equal((Errors.Unavailable, typeof(NotFoundException)), (error?.Type, error?.Exception?.GetType()));
    }

    [Fact]
    public async Task CheckHealthAsync_CancelledToken_ReturnsOperationCancelled()
    {
        // Arrange
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await stop.CancelAsync();
        _sns.GetTopicAttributesAsync(default(GetTopicAttributesRequest)!, Ct).ThrowsAsyncForAnyArgs(new OperationCanceledException());

        // Act
        var error = await Client.CheckHealthAsync(stop.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, error?.Type);
        await _sns.Received(1).GetTopicAttributesAsync(Arg.Is<GetTopicAttributesRequest>(static r => r.TopicArn == _defaultTopic), stop.Token);
        await _sns.DidNotReceive().GetTopicAttributesAsync(Arg.Is<GetTopicAttributesRequest>(static r => r.TopicArn == _topicOfTheTestMessage), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(WaitHeaderRows))]
    public async Task PublishAsync_ThrottleOfTheRealSdk_ReturnsRateLimitedWithTheWaitOfTheHeaders(
        string row, string? amazonHeader, string? standardHeader, double? expectedSeconds)
    {
        // Arrange
        var headers = new Dictionary<string, string?> { ["x-amz-retry-after"] = amazonHeader, ["Retry-After"] = standardHeader }
            .Where(static header => header.Value is not null)
            .ToDictionary(static header => header.Key, static header => header.Value!, StringComparer.Ordinal);
        using var sns = FakeSnsHttp.ClientThatAnswers(HttpStatusCode.BadRequest, "Throttled", headers);
        var client = new AmazonSnsBrokerClient(sns, Options.Create(AnOptions()));

        // Act
        var result = await client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal(
            (row, Errors.RateLimited, expectedSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null),
            (row, result.Error?.Type, result.Error?.RetryAfter));
    }

    private static AmazonSimpleNotificationServiceException AnHttpError(int statusCode) =>
        new("HTTP error.", AwsErrorType.Receiver, "InternalFailure", "request-1", (HttpStatusCode)statusCode);

    private static AmazonSimpleNotificationServiceException AServiceError(string errorCode, int statusCode) =>
        new("Rate exceeded.", AwsErrorType.Sender, errorCode, "request-1", (HttpStatusCode)statusCode);

    private static AmazonSnsOptions AnOptions() => new()
    {
        DefaultTopicArn = _defaultTopic,
        Topics = { ["TestMessage"] = _topicOfTheTestMessage },
    };

    private static Dictionary<string, string> HeadersOf(int count) =>
        Enumerable.Range(1, count).ToDictionary(static i => $"x-header-{i}", static i => $"value-{i}", StringComparer.Ordinal);
}
