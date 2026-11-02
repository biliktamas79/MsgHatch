using MsgHatch.Domain.Failures;

namespace MsgHatch.Email.Tests.Unit.Contracts;

/// <summary>
/// The driver of <see cref="EmailClientTestsBase{TClient}"/>: it makes the email provider behind the client succeed,
/// fail, or wait (section 5.5).
/// </summary>
public interface IEmailClientTestDriver
{
    /// <summary>Makes the provider accept the next email with the given message id of the provider.</summary>
    void Accepts(string providerMessageId);

    /// <summary>Makes the provider fail the next email, so that the client gives an error of the given type.</summary>
    void Fails(ErrorType type);

    /// <summary>Makes the provider wait in the next send until its token is cancelled.</summary>
    void WaitsUntilCancelled();

    /// <summary>Makes the provider accept emails at the next health check.</summary>
    void CanSend();

    /// <summary>Makes the provider refuse each email at the next health check.</summary>
    void CannotSend();
}
