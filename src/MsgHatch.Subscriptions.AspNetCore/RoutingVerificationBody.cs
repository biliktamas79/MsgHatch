namespace MsgHatch.Subscriptions.AspNetCore;

/// <summary>The body of <c>POST {prefix}/verification</c>: the input of a routing verification (section 4.18).</summary>
/// <param name="TestUsers">The ids of the test users, for example <c>["1", "3"]</c>.</param>
/// <param name="Languages">The UI languages of the test profiles, for example <c>["en", "hu"]</c>.</param>
/// <param name="MessageType">Only this message type, or null for each message type with a scope function.</param>
public sealed record RoutingVerificationBody(IReadOnlyList<string> TestUsers, IReadOnlyList<string> Languages, string? MessageType = null);
