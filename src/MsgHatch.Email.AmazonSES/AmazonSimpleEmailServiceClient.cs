using System.Net;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using AmazonServiceException = Amazon.Runtime.AmazonServiceException;

namespace MsgHatch.Email.AmazonSES;

/// <summary>
/// The Amazon SES implementation of <see cref="IEmailClient"/>, on the SES API version 2. It converts each exception of
/// the AWS SDK into an <see cref="Error"/>, and does not throw (section 4.12). An error message names only the exception
/// type, because the text of an SES exception can contain an address.
/// </summary>
/// <param name="ses">The SES client of the AWS SDK.</param>
/// <param name="options">The settings.</param>
/// <param name="logger">The logger of the account errors, or null for no log.</param>
public sealed class AmazonSimpleEmailServiceClient(
    IAmazonSimpleEmailServiceV2 ses, IOptions<AmazonSesOptions> options, ILogger<AmazonSimpleEmailServiceClient>? logger = null)
    : IEmailClient
{
    /// <summary>The name of the email tag that holds the outgoing message id.</summary>
    public const string MessageIdTag = "msghatch-id";

    private const string _utf8 = "UTF-8";

    private readonly ILogger _logger = logger ?? NullLogger<AmazonSimpleEmailServiceClient>.Instance;

    /// <inheritdoc />
    public async ValueTask<ErrorOr<PublishReceipt>> SendAsync(Guid messageId, EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            var response = await ses.SendEmailAsync(RequestOf(messageId, message), ct);
            return new PublishReceipt(response.MessageId);
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    /// <summary>
    /// Reads the account of SES. SES refuses each email of an account with <c>SendingEnabled</c> false, so the channel
    /// must stay down (D106).
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null when SES accepts emails, else the error.</returns>
    public async ValueTask<Error?> CheckHealthAsync(CancellationToken ct)
    {
        try
        {
            var account = await ses.GetAccountAsync(new GetAccountRequest(), ct);
            return account.SendingEnabled is false ? new Error(Errors.Unavailable, "Amazon SES: sending is disabled for the account.") : null;
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    private SendEmailRequest RequestOf(Guid messageId, EmailMessage message) => new()
    {
        FromEmailAddress = message.From ?? options.Value.SenderAddress,
        Destination = new Destination { ToAddresses = [message.To], CcAddresses = ListOf(message.Cc), BccAddresses = ListOf(message.Bcc) },
        Content = new EmailContent
        {
            Simple = new Message
            {
                Subject = Utf8ContentOf(message.Subject),
                Body = new Body { Html = Utf8ContentOf(message.HtmlBody), Text = Utf8ContentOf(message.TextBody) },
            },
        },
        EmailTags = [new MessageTag { Name = MessageIdTag, Value = messageId.ToString() }],
    };

    private static List<string>? ListOf(IReadOnlyList<string>? addresses) => addresses is null ? null : [.. addresses];

    private static Content? Utf8ContentOf(string? text) => text is null ? null : new Content { Data = text, Charset = _utf8 };

    private Error ErrorOf(Exception exception, CancellationToken ct) => exception switch
    {
        OperationCanceledException when ct.IsCancellationRequested => new Error(Errors.OperationCancelled),
        TooManyRequestsException or LimitExceededException => SesError(Errors.RateLimited, exception),
        SendingPausedException or AccountSuspendedException => AccountErrorOf(exception),
        HttpRequestException or AmazonServiceException { StatusCode: >= HttpStatusCode.InternalServerError } =>
            SesError(Errors.Unavailable, exception),
        MessageRejectedException or BadRequestException or MailFromDomainNotVerifiedException or NotFoundException =>
            SesError(Errors.Rejected, exception),
        _ => SesError(Errors.Exception, exception),
    };

    private Error AccountErrorOf(Exception exception)
    {
        var reason = exception.GetType().Name;
        Log.AccountCannotSend(_logger, reason);
        return SesError(Errors.Unavailable, exception);
    }

    private static Error SesError(ErrorType type, Exception exception) =>
        new(type, $"Amazon SES: {exception.GetType().Name}", exception);
}
