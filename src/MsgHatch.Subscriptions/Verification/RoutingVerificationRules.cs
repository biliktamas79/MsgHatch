using MsgHatch.Domain.Failures;

namespace MsgHatch.Subscriptions.Verification;

/// <summary>
/// The rules of a routing verification request that need no state (D234, D248). Each implementation of
/// <see cref="IRoutingVerifier"/> runs them first, so each implementation gives the same first error for a request, and
/// the HTTP client gives it with no HTTP call.
/// </summary>
public static class RoutingVerificationRules
{
    /// <summary>Checks a request: the test users and the languages are lists, and no language is null, empty, or white space.</summary>
    /// <param name="request">The request.</param>
    /// <returns>Null, or <see cref="Errors.ValidationFailed"/>.</returns>
    /// <exception cref="ArgumentNullException">The request is null.</exception>
    public static Error? Check(RoutingVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.TestUsers is null || request.Languages is null || request.Languages.Any(string.IsNullOrWhiteSpace)
            ? new Error(Errors.ValidationFailed, "A verification needs the test users and the languages, and no language is empty.")
            : null;
    }
}
