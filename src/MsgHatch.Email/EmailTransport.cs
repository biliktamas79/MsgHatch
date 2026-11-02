using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Email;

/// <summary>
/// The transport of the email channel (D129). It sends the <see cref="EmailMessage"/> of each outgoing message through
/// <see cref="IEmailClient"/>. A redirect address replaces the recipient, and removes the copies (section 4.8).
/// </summary>
/// <param name="client">The email client.</param>
public sealed class EmailTransport(IEmailClient client) : ITransport
{
    /// <inheritdoc />
    public ChannelId Channel => EmailChannel.Id;

    /// <inheritdoc />
    public ValueTask<ErrorOr<PublishReceipt>> SendAsync(OutgoingMessage message, string? redirectTo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Content is not EmailMessage email)
        {
            return ValueTask.FromResult<ErrorOr<PublishReceipt>>(
                new Error(Errors.ValidationFailed, $"The content of an email is not an {nameof(EmailMessage)}."));
        }

        return client.SendAsync(message.Delivery.Id, redirectTo is null ? email : Redirected(email, redirectTo), ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => client.CheckHealthAsync(ct);

    private static EmailMessage Redirected(EmailMessage email, string redirectTo) => email with { To = redirectTo, Cc = null, Bcc = null };
}
