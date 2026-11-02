using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Resend.Tests.Unit.TestTypes;
using MsgHatch.Email.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Resend.Tests.Unit;

/// <summary>
/// The Resend client: the use cases of the email client contract, and the rules that only Resend has: the request, the
/// sender address, the error of each Resend response, and the health check of the API key.
/// </summary>
public sealed class ResendEmailClientUnitTests : EmailClientTestsBase<ResendEmailClient>
{
    private const string _address = "client1@example.com";
    private const string _apiKey = "re_test_123";

    private readonly FakeResendApi _api;
    private readonly FakeLogger<ResendEmailClient> _logger;

    public ResendEmailClientUnitTests()
        : this(new ResendTestDriver(), new FakeLogger<ResendEmailClient>())
    {
    }

    private ResendEmailClientUnitTests(ResendTestDriver driver, FakeLogger<ResendEmailClient> logger)
        : base(new ResendEmailClient(driver.Api.Clients(), Options.Create(AnOptions()), logger), driver)
    {
        _api = driver.Api;
        _logger = logger;
    }

    public static TheoryData<string, int, string, int> ErrorResponseRows => new()
    {
        { "a validation error", 400, FakeResendApi.ErrorBody(400, "validation_error", "The `to` field is invalid."), 403 },
        { "a missing field", 422, FakeResendApi.ErrorBody(422, "missing_required_field", "The `to` field is missing."), 403 },
        { "a recipient that the resend.dev sender cannot reach", 403, FakeResendApi.ErrorBody(403, "validation_error", $"You can only send testing emails to your own email address ({_address})."), 403 },
        { "a changed body under the same idempotency key", 409, FakeResendApi.ErrorBody(409, "invalid_idempotent_request", "The body does not match."), 403 },
        { "a send in progress under the same idempotency key", 409, FakeResendApi.ErrorBody(409, "concurrent_idempotent_requests", "Another request is in progress."), 503 },
        { "a missing API key", 401, FakeResendApi.ErrorBody(401, "missing_api_key", "Missing API key."), 503 },
        { "an invalid API key", 401, FakeResendApi.ErrorBody(401, "validation_error", "API key is invalid"), 503 },
        { "an inactive API key", 403, FakeResendApi.ErrorBody(403, "restricted_api_key", "API key is not active"), 503 },
        { "a suspended API key", 403, FakeResendApi.ErrorBody(403, "suspended_api_key", "This API key is suspended"), 503 },
        { "the rate limit", 429, FakeResendApi.ErrorBody(429, "rate_limit_exceeded", "Too many requests."), 429 },
        { "the daily quota", 429, FakeResendApi.ErrorBody(429, "daily_quota_exceeded", "You have exceeded your daily email sending quota."), 429 },
        { "an HTTP 499 response with no body", 499, "", 403 },
        { "an HTTP 500 response", 500, FakeResendApi.ErrorBody(500, "application_error", "An unexpected error occurred."), 503 },
        { "an HTTP 502 response with an HTML body", 502, "<html>Bad gateway</html>", 503 },
        { "an HTTP 503 response", 503, FakeResendApi.ErrorBody(503, "service_unavailable", "API is temporarily unavailable"), 503 },
    };

    public static TheoryData<string, Exception, int> ExceptionRows => new()
    {
        { "no network", new HttpRequestException("No connection."), 503 },
        { "a timeout of the HTTP client", new TaskCanceledException("The request timed out."), 500 },
        { "another exception", new InvalidOperationException("Defect."), 500 },
    };

    public static TheoryData<string, int, string> ApiKeyErrorRows => new()
    {
        { "a missing API key", 401, FakeResendApi.ErrorBody(401, "missing_api_key", $"Missing API key for {_address}.") },
        { "a suspended API key", 403, FakeResendApi.ErrorBody(403, "suspended_api_key", $"The API key of {_address} is suspended.") },
    };

    public static TheoryData<string, int, string, int> HealthErrorRows => new()
    {
        { "the rate limit", 429, FakeResendApi.ErrorBody(429, "rate_limit_exceeded", "Too many requests."), 429 },
        { "an invalid API key", 400, FakeResendApi.ErrorBody(400, "validation_error", "API key is invalid"), 503 },
        { "an inactive API key", 403, FakeResendApi.ErrorBody(403, "restricted_api_key", "API key is not active"), 503 },
        { "an HTTP 500 response", 500, FakeResendApi.ErrorBody(500, "application_error", "An unexpected error occurred."), 503 },
    };

    public static TheoryData<string, string, string?, string> SenderAddressRows => new()
    {
        { "no sender name", "noreply@example.com", null, "noreply@example.com" },
        { "an empty sender name", "noreply@example.com", "", "noreply@example.com" },
        { "a sender name of only whitespace", "noreply@example.com", "   ", "noreply@example.com" },
        { "a sender name", "noreply@example.com", "Example", "Example <noreply@example.com>" },
        { "a sender name with accented letters", "noreply@example.com", "Példa Kft.", "Példa Kft. <noreply@example.com>" },
        { "an address with < and >", "Example <noreply@example.com>", "Other", "Example <noreply@example.com>" },
    };

    [Fact]
    public async Task SendAsync_Email_PostsOneRequestWithTheJsonOfTheEmail()
    {
        // Arrange
        var id = Guid.NewGuid();
        string[] cc = ["cc1@example.com", "cc2@example.com"];
        string[] bcc = ["bcc1@example.com", "bcc2@example.com"];
        var email = TestMessages.AnEmail(subject: "Ödön Űr rendelése", cc: cc, bcc: bcc);
        var expected = new JsonObject
        {
            ["from"] = "Example <noreply@example.com>",
            ["to"] = new JsonArray(email.To),
            ["subject"] = email.Subject,
            ["html"] = email.HtmlBody,
            ["text"] = email.TextBody,
            ["cc"] = new JsonArray([.. cc.Select(static address => JsonValue.Create(address))]),
            ["bcc"] = new JsonArray([.. bcc.Select(static address => JsonValue.Create(address))]),
            ["tags"] = new JsonArray(new JsonObject { ["name"] = "msghatch-id", ["value"] = id.ToString() }),
        };

        // Act
        await Client.SendAsync(id, email, Ct);

        // Assert
        var request = Assert.Single(_api.Requests);
        Assert.Equal(
            (HttpMethod.Post, "https://api.resend.com/emails", expected.ToJsonString()),
            (request.Method, request.Uri?.ToString(), request.Json?.ToJsonString()));
    }

    [Fact]
    public async Task SendAsync_EmailWithNoHtmlBodyAndNoCopies_SendsNoFieldForThem()
    {
        // Arrange
        var id = Guid.NewGuid();
        var email = TestMessages.AnEmail() with { HtmlBody = null };
        var expected = new JsonObject
        {
            ["from"] = "Example <noreply@example.com>",
            ["to"] = new JsonArray(email.To),
            ["subject"] = email.Subject,
            ["text"] = email.TextBody,
            ["tags"] = new JsonArray(new JsonObject { ["name"] = "msghatch-id", ["value"] = id.ToString() }),
        };

        // Act
        await Client.SendAsync(id, email, Ct);

        // Assert
        Assert.Equal(expected.ToJsonString(), Assert.Single(_api.Requests).Json?.ToJsonString());
    }

    [Fact]
    public async Task SendAsync_OutgoingMessageId_SendsItAsTheIdempotencyKey()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        await Client.SendAsync(id, TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal(id.ToString(), Assert.Single(_api.Requests).Header("Idempotency-Key"));
    }

    [Fact]
    public async Task SendAsync_Email_SendsTheApiKeyAsABearerTokenAndAUserAgent()
    {
        // Act
        await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        var request = Assert.Single(_api.Requests);
        Assert.Equal(($"Bearer {_apiKey}", false), (request.Header("Authorization"), string.IsNullOrWhiteSpace(request.Header("User-Agent"))));
    }

    [Theory]
    [MemberData(nameof(SenderAddressRows))]
    public async Task SendAsync_SenderOfTheOptions_SendsFromTheSenderAddress(string row, string senderEmail, string? senderName, string expected)
    {
        // Arrange
        var client = new ResendEmailClient(_api.Clients(), Options.Create(AnOptions(senderEmail, senderName)));

        // Act
        await client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal((row, expected), (row, Assert.Single(_api.Requests).Json?["from"]?.GetValue<string>()));
    }

    [Fact]
    public async Task SendAsync_EmailWithASenderAddress_SendsFromTheAddressOfTheEmail()
    {
        // Arrange
        var email = TestMessages.AnEmail() with { From = "Sales <sales@example.com>" };

        // Act
        await Client.SendAsync(Guid.NewGuid(), email, Ct);

        // Assert
        Assert.Equal(email.From, Assert.Single(_api.Requests).Json?["from"]?.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(ErrorResponseRows))]
    public async Task SendAsync_ErrorResponse_ReturnsTheErrorTypeOfTheResponse(string row, int status, string body, int expectedCode)
    {
        // Arrange
        _api.RespondsWith((HttpStatusCode)status, body);

        // Act
        var result = await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal((row, (int?)expectedCode), (row, result.Error?.Type.Code));
    }

    [Theory]
    [MemberData(nameof(ExceptionRows))]
    public async Task SendAsync_Exception_ReturnsTheErrorTypeOfTheException(string row, Exception exception, int expectedCode)
    {
        // Arrange
        _api.Throws(exception);

        // Act
        var result = await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal((row, (int?)expectedCode), (row, result.Error?.Type.Code));
    }

    [Theory]
    [MemberData(nameof(ApiKeyErrorRows))]
    public async Task SendAsync_ErrorOfTheApiKey_LogsACriticalRecordWithNoAddress(string row, int status, string body)
    {
        // Arrange
        _api.RespondsWith((HttpStatusCode)status, body);

        // Act
        await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        var record = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal((row, LogLevel.Critical, 3000, false), (row, record.Level, record.Id.Id, record.Message.Contains(_address, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SendAsync_RejectedEmail_LogsNothing()
    {
        // Arrange
        Driver.Fails(Errors.Rejected);

        // Act
        await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task SendAsync_ErrorResponseWithAnAddress_ReturnsAnErrorMessageWithNoAddress()
    {
        // Arrange
        _api.RespondsWith(HttpStatusCode.Forbidden, FakeResendApi.ErrorBody(403, "validation_error", $"You can only send testing emails to your own email address ({_address})."));

        // Act
        var result = await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.DoesNotContain(_address, result.Error?.Message ?? _address, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckHealthAsync_Request_GetsTheDomainsWithTheApiKey()
    {
        // Arrange
        Driver.CanSend();

        // Act
        await Client.CheckHealthAsync(Ct);

        // Assert
        var request = Assert.Single(_api.Requests);
        Assert.Equal(
            (HttpMethod.Get, "https://api.resend.com/domains", $"Bearer {_apiKey}", false),
            (request.Method, request.Uri?.ToString(), request.Header("Authorization"), string.IsNullOrWhiteSpace(request.Header("User-Agent"))));
    }

    [Fact]
    public async Task CheckHealthAsync_ApiKeyThatCanOnlySend_ReturnsNull()
    {
        // Arrange
        _api.RespondsWith(HttpStatusCode.Unauthorized, FakeResendApi.ErrorBody(401, "restricted_api_key", "This API key is restricted to only send emails."));

        // Act
        var error = await Client.CheckHealthAsync(Ct);

        // Assert
        Assert.Null(error);
    }

    [Theory]
    [MemberData(nameof(HealthErrorRows))]
    public async Task CheckHealthAsync_ErrorResponse_ReturnsTheErrorTypeOfTheResponse(string row, int status, string body, int expectedCode)
    {
        // Arrange
        _api.RespondsWith((HttpStatusCode)status, body);

        // Act
        var error = await Client.CheckHealthAsync(Ct);

        // Assert
        Assert.Equal((row, (int?)expectedCode), (row, error?.Type.Code));
    }

    [Fact]
    public async Task CheckHealthAsync_NoNetwork_ReturnsUnavailable()
    {
        // Arrange
        _api.Throws(new HttpRequestException("No connection."));

        // Act
        var error = await Client.CheckHealthAsync(Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    private static ResendOptions AnOptions(string senderEmail = "noreply@example.com", string? senderName = "Example") =>
        new() { ApiKey = _apiKey, SenderEmail = senderEmail, SenderName = senderName };
}
