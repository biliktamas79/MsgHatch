using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Email.Resend;

/// <summary>
/// The Resend implementation of <see cref="IEmailClient"/>, on the REST API of Resend. It converts each error response
/// and each exception into an <see cref="Error"/>, and does not throw (section 4.12). An error message names only the
/// HTTP status and the error name of Resend, because the message of a Resend error can contain an address.
/// </summary>
/// <param name="httpClients">The factory of the HTTP client <see cref="HttpClientName"/>.</param>
/// <param name="options">The settings.</param>
/// <param name="logger">The logger of the API key errors, or null for no log.</param>
public sealed class ResendEmailClient(
    IHttpClientFactory httpClients, IOptions<ResendOptions> options, ILogger<ResendEmailClient>? logger = null)
    : IEmailClient
{
    /// <summary>The name of the HTTP client of the Resend API.</summary>
    public const string HttpClientName = "MsgHatch.Resend";

    /// <summary>The name of the email tag that holds the outgoing message id.</summary>
    public const string MessageIdTag = "msghatch-id";

    // Resend refuses a request with no User-Agent header (HTTP 403). It keeps an Idempotency-Key for 24 hours, and gives
    // the first response again for a repeated request, without a second email.
    private const string _idempotencyKey = "Idempotency-Key";

    private static readonly Uri _emails = new("https://api.resend.com/emails");
    private static readonly Uri _domains = new("https://api.resend.com/domains");
    private static readonly ProductInfoHeaderValue _userAgent =
        new(typeof(ResendEmailClient).Assembly.GetName().Name!, typeof(ResendEmailClient).Assembly.GetName().Version!.ToString(3));

    private readonly ILogger _logger = logger ?? NullLogger<ResendEmailClient>.Instance;

    /// <inheritdoc />
    public async ValueTask<ErrorOr<PublishReceipt>> SendAsync(Guid messageId, EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            using var request = RequestOf(HttpMethod.Post, _emails);
            request.Headers.Add(_idempotencyKey, messageId.ToString());
            request.Content = JsonContent.Create(BodyOf(messageId, message), ResendJson.Default.ResendEmail);
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return SendErrorOf(response.StatusCode, await ErrorNameOf(response, ct));
            }

            var sent = await response.Content.ReadFromJsonAsync(ResendJson.Default.ResendSentEmail, ct);
            return new PublishReceipt(sent?.Id);
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    /// <summary>
    /// Reads the domains of the account. Resend answers a key that can only send with 401 <c>restricted_api_key</c>,
    /// which proves that the key is valid and that the API answers.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null when Resend accepts emails of the key, else the error.</returns>
    public async ValueTask<Error?> CheckHealthAsync(CancellationToken ct)
    {
        try
        {
            using var request = RequestOf(HttpMethod.Get, _domains);
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var name = await ErrorNameOf(response, ct);
            return IsAKeyThatCanOnlySend(response.StatusCode, name) ? null : HealthErrorOf(response.StatusCode, name);
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    private HttpRequestMessage RequestOf(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        request.Headers.UserAgent.Add(_userAgent);
        return request;
    }

    private ResendEmail BodyOf(Guid messageId, EmailMessage message) => new(
        message.From ?? options.Value.SenderAddress, [message.To], message.Subject, message.HtmlBody, message.TextBody,
        ArrayOf(message.Cc), ArrayOf(message.Bcc), [new ResendTag(MessageIdTag, messageId.ToString())]);

    private static string[]? ArrayOf(IReadOnlyList<string>? addresses) => addresses is null ? null : [.. addresses];

    private static async Task<string?> ErrorNameOf(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync(ResendJson.Default.ResendError, ct);
            return error?.Name;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsAKeyThatCanOnlySend(HttpStatusCode status, string? name) =>
        status == HttpStatusCode.Unauthorized && name == "restricted_api_key";

    private static bool IsAnErrorOfTheApiKey(HttpStatusCode status, string? name) =>
        status == HttpStatusCode.Unauthorized
        || (status == HttpStatusCode.Forbidden && name is "restricted_api_key" or "suspended_api_key");

    private Error SendErrorOf(HttpStatusCode status, string? name) => status switch
    {
        HttpStatusCode.TooManyRequests => ResendError(Errors.RateLimited, status, name),
        >= HttpStatusCode.InternalServerError => ResendError(Errors.Unavailable, status, name),
        _ when IsAnErrorOfTheApiKey(status, name) => ApiKeyErrorOf(status, name),
        HttpStatusCode.Conflict when name == "concurrent_idempotent_requests" => ResendError(Errors.Unavailable, status, name),
        _ => ResendError(Errors.Rejected, status, name),
    };

    private static Error HealthErrorOf(HttpStatusCode status, string? name) =>
        ResendError(status == HttpStatusCode.TooManyRequests ? Errors.RateLimited : Errors.Unavailable, status, name);

    private Error ApiKeyErrorOf(HttpStatusCode status, string? name)
    {
        var reason = name ?? ((int)status).ToString(CultureInfo.InvariantCulture);
        Log.ApiKeyCannotSend(_logger, reason);
        return ResendError(Errors.Unavailable, status, name);
    }

    private static Error ResendError(ErrorType type, HttpStatusCode status, string? name) =>
        new(type, $"Resend: HTTP {(int)status} {name}".TrimEnd());

    private static Error ErrorOf(Exception exception, CancellationToken ct) => exception switch
    {
        OperationCanceledException when ct.IsCancellationRequested => new Error(Errors.OperationCancelled),
        HttpRequestException => new Error(Errors.Unavailable, $"Resend: {nameof(HttpRequestException)}", exception),
        _ => new Error(Errors.Exception, $"Resend: {exception.GetType().Name}", exception),
    };
}
