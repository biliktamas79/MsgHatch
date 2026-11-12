using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The subscription data of a message type with no generic type, so that the subscription manager can list the message
/// types with a scope function (D81), and the routing verifier can check their test cases (section 4.18).
/// </summary>
internal interface ISubscriptionMessage
{
    Type EventType { get; }

    bool HasScope { get; }

    bool HasTestCases { get; }

    ValueTask<Error?> VerifyAsync(RoutingVerifier verifier, VerificationRun run, MessageType type, CancellationToken ct);
}
