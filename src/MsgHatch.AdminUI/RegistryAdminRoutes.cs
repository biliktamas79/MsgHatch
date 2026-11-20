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

    internal const string ChannelEditTemplate = ChannelTemplate + "/edit";

    internal const string MessageTypeEditTemplate = MessageTypes + "/{TypeName}/edit";

    /// <summary>Gives the address of the view page of one channel.</summary>
    /// <param name="channel">The id of the channel.</param>
    /// <returns>For example <c>/msghatch/channels/Email</c>.</returns>
    public static string Channel(ChannelId channel) => $"{Channels}/{Uri.EscapeDataString(channel.Name)}";

    /// <summary>Gives the address of the edit page of the stored settings of one channel (D287).</summary>
    /// <param name="channel">The id of the channel.</param>
    /// <returns>For example <c>/msghatch/channels/Email/edit</c>.</returns>
    public static string ChannelEdit(ChannelId channel) => $"{Channel(channel)}/edit";

    /// <summary>Gives the address of the edit page of the stored settings of one message type (D287).</summary>
    /// <param name="type">The message type.</param>
    /// <returns>For example <c>/msghatch/message-types/Invoices/edit</c>.</returns>
    public static string MessageTypeEdit(MessageType type) => $"{MessageTypes}/{Uri.EscapeDataString(type.Name)}/edit";
}
