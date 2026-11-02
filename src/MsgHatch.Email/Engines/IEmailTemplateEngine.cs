using MsgHatch.Domain.Failures;

namespace MsgHatch.Email.Engines;

/// <summary>
/// Finds and prepares the email templates of one payload type (D44, section 4.10). An engine is a tenant service
/// (D126). A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
public interface IEmailTemplateEngine<TPayload>
{
    /// <summary>Finds and prepares the template for one message type, language, and template scope.</summary>
    /// <param name="request">The message type, the language, and the template scope.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The prepared template, or <see cref="Errors.NotFound"/> when no template exists.</returns>
    ValueTask<ErrorOr<IEmailTemplate<TPayload>>> PrepareAsync(EmailTemplateRequest request, CancellationToken ct);
}
