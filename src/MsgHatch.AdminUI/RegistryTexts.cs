using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AdminUI;

/// <summary>The texts of the registry pages (D272, D273).</summary>
internal static class RegistryTexts
{
    private static readonly ChannelFeatures[] _features =
        [ChannelFeatures.Languages, ChannelFeatures.RichText, ChannelFeatures.RawData, ChannelFeatures.GroupDelivery];

    /// <summary>Gives the name of each feature of a channel, in the order of <see cref="ChannelFeatures"/>.</summary>
    public static IEnumerable<string> FeaturesOf(ChannelFeatures features) =>
        _features.Where(feature => features.HasFlag(feature)).Select(static feature => feature.ToString());

    public static string YesOrNo(bool value) => value ? "Yes" : "No";

    /// <summary>Gives the state of a message type on one of its channels. A disabled message type sends on no channel.</summary>
    public static string StateOn(RegistryMessageType type, ChannelId channel) =>
        !type.Enabled ? "Message type disabled"
        : type.DisabledChannels.Contains(channel) ? "Disabled on this channel"
        : "Enabled";
}
