using MsgHatch.Channels.Email;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace ExampleApp.Channels;

/// <summary>
/// The email client of the example. It writes each email to the log, in place of an email provider, so the example
/// needs no SMTP server and no cloud account. A real provider client never logs an address or a text (D37).
/// </summary>
/// <param name="logger">The logger.</param>
public sealed class LogEmailClient(ILogger<LogEmailClient> logger) : IEmailClient
{
    /// <inheritdoc/>
    public ValueTask<ErrorOr<PublishReceipt>> SendAsync(Guid messageId, EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        logger.EmailSent(messageId, message.To, message.Subject, message.TextBody ?? message.HtmlBody);
        return ValueTask.FromResult(ErrorOr<PublishReceipt>.ForValue(new PublishReceipt($"log-{messageId}")));
    }

    /// <inheritdoc/>
    public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => ValueTask.FromResult<Error?>(null);
}
