using MsgHatch.Domain.Identity;

namespace MsgHatch.AdminUI;

/// <summary>The addresses of the registry pages of the core admin UI, so that a host can link to a page (D272).</summary>
public static class RegistryAdminRoutes
{
    /// <summary>The address of the channel list.</summary>
    public const string Channels = "/msghatch/channels";

    /// <summary>The address of the message type list (D273).</summary>
    public const string MessageTypes = "/msghatch/message-types";

    internal const string ChannelTemplate = Channels + "/{ChannelName}";

    /// <summary>Gives the address of the view page of one channel.</summary>
    /// <param name="channel">The id of the channel.</param>
    /// <returns>For example <c>/msghatch/channels/Email</c>.</returns>
    public static string Channel(ChannelId channel) => $"{Channels}/{Uri.EscapeDataString(channel.Name)}";
}
