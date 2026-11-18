namespace MsgHatch;

/// <summary>
/// The route paths of the subscription management API under its prefix (section 4.17, D228). The <c>.AspNetCore</c>
/// package and the client package compile this file, so a change of a route changes both sides in one commit.
/// </summary>
internal static class SubscriptionApiRoutes
{
    public const string Channels = "/channels";
    public const string MessageTypes = "/message-types";
    public const string Subscriptions = "";
    public const string Subscription = "/{id:guid}";
    public const string Verification = "/verification";
    public const string Me = "/me";
}
