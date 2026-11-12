using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Verification;

/// <summary>The input of a routing verification (D85, section 4.18).</summary>
/// <param name="TestUsers">The real users of the recipient directory for whom the verifier makes the test events.</param>
/// <param name="Languages">The UI languages of the test profiles. Each language gives one variation for each test user.</param>
/// <param name="Type">Only this message type, or null for each message type with a scope function.</param>
public sealed record RoutingVerificationRequest(
    IReadOnlyList<UserId> TestUsers, IReadOnlyList<string> Languages, MessageType? Type = null);
