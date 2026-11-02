using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Email;

/// <summary>
/// The client of an email provider, for example Amazon SES (section 4.6). The email transport sends each email through
/// it. A client returns an <see cref="Error"/> for a failure, and does not throw. A caller must not await one returned
/// <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface IEmailClient
{
    /// <summary>Sends one email.</summary>
    /// <param name="messageId">The id of the outgoing message of the email.</param>
    /// <param name="message">The email.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The receipt of the provider, or the error.</returns>
    ValueTask<ErrorOr<PublishReceipt>> SendAsync(Guid messageId, EmailMessage message, CancellationToken ct);

    /// <summary>Checks that the provider accepts emails.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null when the provider accepts emails, else the error.</returns>
    ValueTask<Error?> CheckHealthAsync(CancellationToken ct);
}
