using MsgHatch.Domain.Failures;

namespace MsgHatch.Subscriptions.Verification;

/// <summary>
/// The full routing test of the subscription router (D85, section 4.18). It resolves and composes the messages of each
/// message type with a scope function, for each test case, test user, and language, with previews only, so it stores
/// nothing and sends nothing (D100). It is a tenant service, on the subscription store and the recipient directory of
/// its key, so one verification checks one tenant (D116, D126). A caller must not await one <see cref="ValueTask"/> two times.
/// </summary>
public interface IRoutingVerifier
{
    /// <summary>
    /// Runs the verification. A failed check is a row of the report, not an error. An error of the store or the
    /// directory before the checks, or a cancellation, gives the error.
    /// </summary>
    /// <param name="request">The test users, the languages, and the optional message type.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The report, or an error.</returns>
    ValueTask<ErrorOr<RoutingVerificationReport>> VerifyAsync(RoutingVerificationRequest request, CancellationToken ct);
}
