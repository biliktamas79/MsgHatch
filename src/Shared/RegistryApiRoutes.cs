namespace MsgHatch;

/// <summary>
/// The route paths of the registry API under its prefix (section 4.20, D270). <c>MsgHatch.AspNetCore</c> and the client
/// package compile this file, so a change of a route changes both sides in one commit.
/// </summary>
internal static class RegistryApiRoutes
{
    public const string Channels = "/channels";
    public const string Channel = Channels + "/{channel}";
    public const string ChannelHistory = Channel + "/history";
    public const string ChannelCheck = Channel + "/check";
    public const string MessageTypes = "/message-types";
    public const string MessageType = MessageTypes + "/{type}";
}
