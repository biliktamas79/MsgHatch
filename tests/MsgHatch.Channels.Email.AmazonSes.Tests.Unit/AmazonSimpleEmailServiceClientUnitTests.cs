using System.Net;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using MsgHatch.Channels.Email.AmazonSes.Tests.Unit.TestTypes;
using MsgHatch.Channels.Email.Tests.Unit.Contracts;
using MsgHatch.Channels.Email.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using AwsErrorType = Amazon.Runtime.ErrorType;
using SesConflictClassifier = MsgHatch.Domain.Tests.Unit.TestTypes.ConflictClassifier<MsgHatch.Channels.Email.AmazonSes.AmazonSimpleEmailServiceClient>;

namespace MsgHatch.Channels.Email.AmazonSes.Tests.Unit;

/// <summary>
/// The Amazon SES client: the use cases of the email client contract, and the rules that only SES has: the request, the
/// sender address, the error of each SES exception, and the health check of the account (section 4.4, phase 4).
/// </summary>
public sealed class AmazonSimpleEmailServiceClientUnitTests : EmailClientTestsBase<AmazonSimpleEmailServiceClient>
{
    private const string _address = "client1@example.com";

    private readonly IAmazonSimpleEmailServiceV2 _ses;
    private readonly FakeLogger<AmazonSimpleEmailServiceClient> _logger;

    public AmazonSimpleEmailServiceClientUnitTests()
        : this(new SesTestDriver(), new FakeLogger<AmazonSimpleEmailServiceClient>())
    {
    }

    private AmazonSimpleEmailServiceClientUnitTests(SesTestDriver driver, FakeLogger<AmazonSimpleEmailServiceClient> logger)
        : base(new AmazonSimpleEmailServiceClient(driver.Ses, Options.Create(AnOptions()), logger), driver)
    {
        _ses = driver.Ses;
        _logger = logger;
    }

    public static TheoryData<string, Exception, int> ExceptionRows => new()
    {
        { "too many requests", new TooManyRequestsException("Too many requests."), 429 },
        { "a limit", new LimitExceededException("Limit exceeded."), 429 },
        { "sending paused", new SendingPausedException("Sending is paused."), 503 },
        { "account suspended", new AccountSuspendedException("The account is suspended."), 503 },
        { "no network", new HttpRequestException("No connection."), 503 },
        { "an HTTP 499 response", AnHttpError(499), 500 },
        { "an HTTP 500 response", AnHttpError(500), 503 },
        { "an HTTP 501 response", AnHttpError(501), 503 },
        { "a rejected message", new MessageRejectedException("Rejected."), 403 },
        { "a bad request", new BadRequestException("Bad request."), 403 },
        { "a MAIL FROM domain that is not verified", new MailFromDomainNotVerifiedException("Not verified."), 403 },
        { "an unknown identity", new NotFoundException("Not found."), 403 },
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

    public static TheoryData<string, Exception> AccountErrorRows => new()
    {
        { "sending paused", new SendingPausedException($"Sending is paused for {_address}.") },
        { "account suspended", new AccountSuspendedException($"The account of {_address} is suspended.") },
    };

    public static TheoryData<string, string, string?, string> SenderAddressRows => new()
    {
        { "no sender name", "noreply@example.com", null, "noreply@example.com" },
        { "an empty sender name", "noreply@example.com", "", "noreply@example.com" },
        { "a sender name of only whitespace", "noreply@example.com", "   ", "noreply@example.com" },
        { "a sender name", "noreply@example.com", "Example", "Example <noreply@example.com>" },
        { "a sender name with accented letters", "noreply@example.com", "Példa Kft.", "=?UTF-8?B?UMOpbGRhIEtmdC4=?= <noreply@example.com>" },
        {
            "a name of 44 bytes in one word", "noreply@example.com", "é" + new string('a', 42),
            "=?UTF-8?B?w6lhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWE=?= <noreply@example.com>"
        },
        {
            "a name of 45 bytes in one word", "noreply@example.com", "é" + new string('a', 43),
            "=?UTF-8?B?w6lhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFh?= <noreply@example.com>"
        },
        {
            "a name of 46 bytes in two words", "noreply@example.com", "é" + new string('a', 44),
            "=?UTF-8?B?w6lhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFh?= =?UTF-8?B?YQ==?= <noreply@example.com>"
        },
        {
            "a letter of two bytes after 44 bytes in the next word", "noreply@example.com", new string('a', 44) + "é",
            "=?UTF-8?B?YWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWE=?= =?UTF-8?B?w6k=?= <noreply@example.com>"
        },
        { "an address with < and >", "Example <noreply@example.com>", "Other", "Example <noreply@example.com>" },
    };

    [Fact]
    public async Task SendAsync_Email_SendsOneRequestWithTheAddressesTheSubjectAndTheBodiesInUtf8()
    {
        // Arrange
        Driver.Accepts("ses-1");
        var email = TestMessages.AnEmail(subject: "Ödön Űr rendelése", cc: ["cc1@example.com", "cc2@example.com"], bcc: ["bcc1@example.com", "bcc2@example.com"]);

        // Act
        await Client.SendAsync(Guid.NewGuid(), email, Ct);

        // Assert
        var request = SentRequest();
        Assert.Equal([email.To], request.Destination.ToAddresses);
        Assert.Equal(["cc1@example.com", "cc2@example.com"], request.Destination.CcAddresses);
        Assert.Equal(["bcc1@example.com", "bcc2@example.com"], request.Destination.BccAddresses);
        Assert.Equal(
            ("Ödön Űr rendelése", "UTF-8", email.HtmlBody, "UTF-8", email.TextBody, "UTF-8"),
            (request.Content.Simple.Subject.Data, request.Content.Simple.Subject.Charset, request.Content.Simple.Body.Html.Data,
             request.Content.Simple.Body.Html.Charset, request.Content.Simple.Body.Text.Data, request.Content.Simple.Body.Text.Charset));
    }

    [Fact]
    public async Task SendAsync_OutgoingMessageId_WritesItIntoTheEmailTagMsgHatchId()
    {
        // Arrange
        Driver.Accepts("ses-1");
        var id = Guid.NewGuid();

        // Act
        await Client.SendAsync(id, TestMessages.AnEmail(), Ct);

        // Assert
        var tag = Assert.Single(SentRequest().EmailTags);
        Assert.Equal(("msghatch-id", id.ToString()), (tag.Name, tag.Value));
    }

    [Fact]
    public async Task SendAsync_EmailWithNoHtmlBody_SendsOnlyTheTextBody()
    {
        // Arrange
        Driver.Accepts("ses-1");

        // Act
        await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail() with { HtmlBody = null }, Ct);

        // Assert
        var body = SentRequest().Content.Simple.Body;
        Assert.Equal((true, "Order 1001"), (body.Html is null, body.Text.Data));
    }

    [Theory]
    [MemberData(nameof(SenderAddressRows))]
    public async Task SendAsync_SenderOfTheOptions_SendsFromTheSenderAddress(string row, string senderEmail, string? senderName, string expected)
    {
        // Arrange
        Driver.Accepts("ses-1");
        var client = new AmazonSimpleEmailServiceClient(_ses, Options.Create(new AmazonSesOptions { SenderEmail = senderEmail, SenderName = senderName }));

        // Act
        await client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal((row, expected), (row, SentRequest().FromEmailAddress));
    }

    [Fact]
    public async Task SendAsync_EmailWithASenderAddress_SendsFromTheAddressOfTheEmail()
    {
        // Arrange
        Driver.Accepts("ses-1");

        // Act
        await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail() with { From = "Sales <sales@example.com>" }, Ct);

        // Assert
        Assert.Equal("Sales <sales@example.com>", SentRequest().FromEmailAddress);
    }

    [Theory]
    [MemberData(nameof(ExceptionRows))]
    public async Task SendAsync_SesException_ReturnsTheErrorTypeOfTheException(string row, Exception exception, int expectedCode)
    {
        // Arrange
        _ses.SendEmailAsync(default!, Ct).ThrowsAsyncForAnyArgs(exception);
        var id = Guid.NewGuid();

        // Act
        var result = await Client.SendAsync(id, TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal((row, (int?)expectedCode), (row, result.Error?.Type.Code));
        await _ses.Received(1).SendEmailAsync(Arg.Is<SendEmailRequest>(r => r.EmailTags[0].Value == id.ToString()), Ct);
    }

    [Fact]
    public async Task SendAsync_ClassifierKnowsTheException_ReturnsItsErrorTypeBeforeTheBuiltInMapping()
    {
        // Arrange
        var client = new AmazonSimpleEmailServiceClient(_ses, Options.Create(AnOptions()), classifiers: [new SesConflictClassifier()]);
        _ses.SendEmailAsync(default!, Ct).ThrowsAsyncForAnyArgs(new HttpRequestException());
        var id = Guid.NewGuid();

        // Act
        var result = await client.SendAsync(id, TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal(Errors.Conflict, result.Error?.Type);
        await _ses.Received(1).SendEmailAsync(Arg.Is<SendEmailRequest>(r => r.EmailTags[0].Value == id.ToString()), Ct);
    }

    [Fact]
    public async Task SendAsync_CancelledTokenAndAClassifier_ReturnsOperationCancelled()
    {
        // Arrange
        var client = new AmazonSimpleEmailServiceClient(_ses, Options.Create(AnOptions()), classifiers: [new SesConflictClassifier()]);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await stop.CancelAsync();
        _ses.SendEmailAsync(default!, Ct).ThrowsAsyncForAnyArgs(new OperationCanceledException());
        var id = Guid.NewGuid();

        // Act
        var result = await client.SendAsync(id, TestMessages.AnEmail(), stop.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
        await _ses.Received(1).SendEmailAsync(Arg.Is<SendEmailRequest>(r => r.EmailTags[0].Value == id.ToString()), stop.Token);
    }

    [Theory]
    [MemberData(nameof(AccountErrorRows))]
    public async Task SendAsync_ErrorOfTheAccount_LogsACriticalRecordWithNoAddress(string row, Exception exception)
    {
        // Arrange
        _ses.SendEmailAsync(default!, Ct).ThrowsAsyncForAnyArgs(exception);
        var id = Guid.NewGuid();

        // Act
        await Client.SendAsync(id, TestMessages.AnEmail(), Ct);

        // Assert
        var record = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal((row, LogLevel.Critical, 2100, false), (row, record.Level, record.Id.Id, record.Message.Contains(_address, StringComparison.Ordinal)));
        await _ses.Received(1).SendEmailAsync(Arg.Is<SendEmailRequest>(r => r.EmailTags[0].Value == id.ToString()), Ct);
    }

    [Fact]
    public async Task SendAsync_RejectedMessage_LogsNothing()
    {
        // Arrange
        Driver.Fails(Errors.Rejected);
        var id = Guid.NewGuid();

        // Act
        await Client.SendAsync(id, TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Empty(_logger.Collector.GetSnapshot());
        await _ses.Received(1).SendEmailAsync(Arg.Is<SendEmailRequest>(r => r.EmailTags[0].Value == id.ToString()), Ct);
    }

    [Fact]
    public async Task SendAsync_SesExceptionWithAnAddress_ReturnsAnErrorMessageWithNoAddress()
    {
        // Arrange
        Driver.Fails(Errors.Rejected);
        var id = Guid.NewGuid();

        // Act
        var result = await Client.SendAsync(id, TestMessages.AnEmail(), Ct);

        // Assert
        Assert.DoesNotContain(_address, result.Error?.Message ?? _address, StringComparison.Ordinal);
        await _ses.Received(1).SendEmailAsync(Arg.Is<SendEmailRequest>(r => r.EmailTags[0].Value == id.ToString()), Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_NoNetwork_ReturnsUnavailable()
    {
        // Arrange
        _ses.GetAccountAsync(default!, Ct).ThrowsAsyncForAnyArgs(new HttpRequestException("No connection."));

        // Act
        var error = await Client.CheckHealthAsync(Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
        await _ses.Received(1).GetAccountAsync(Arg.Any<GetAccountRequest>(), Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_ResponseWithNoSendingEnabled_ReturnsNull()
    {
        // Arrange
        _ses.GetAccountAsync(default!, Ct).ReturnsForAnyArgs(Task.FromResult(new GetAccountResponse()));

        // Act
        var error = await Client.CheckHealthAsync(Ct);

        // Assert
        Assert.Null(error);
        await _ses.Received(1).GetAccountAsync(Arg.Any<GetAccountRequest>(), Ct);
    }

    [Theory]
    [MemberData(nameof(WaitHeaderRows))]
    public async Task SendAsync_ThrottleOfTheRealSdk_ReturnsRateLimitedWithTheWaitOfTheHeaders(
        string row, string? amazonHeader, string? standardHeader, double? expectedSeconds)
    {
        // Arrange
        var headers = new Dictionary<string, string?> { ["x-amz-retry-after"] = amazonHeader, ["Retry-After"] = standardHeader }
            .Where(static header => header.Value is not null)
            .ToDictionary(static header => header.Key, static header => header.Value!, StringComparer.Ordinal);
        using var ses = FakeSesHttp.ClientThatAnswers(HttpStatusCode.TooManyRequests, "TooManyRequestsException", headers);
        var client = new AmazonSimpleEmailServiceClient(ses, Options.Create(AnOptions()));

        // Act
        var result = await client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal(
            (row, Errors.RateLimited, expectedSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null),
            (row, result.Error?.Type, result.Error?.RetryAfter));
    }

    private static AmazonSesOptions AnOptions() => new() { SenderEmail = "noreply@example.com", SenderName = "Example" };

    private static AmazonSimpleEmailServiceV2Exception AnHttpError(int statusCode) =>
        new("HTTP error.", AwsErrorType.Receiver, "InternalFailure", "request-1", (HttpStatusCode)statusCode);

    private SendEmailRequest SentRequest() => (SendEmailRequest)_ses.ReceivedCalls().Single().GetArguments()[0]!;
}
