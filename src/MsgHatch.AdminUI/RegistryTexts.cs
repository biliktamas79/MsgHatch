using System.Globalization;
using MsgHatch.Domain.Dispatch;
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

    /// <summary>Gives the reason of a down state (D279): the error name or "Errors" for errors, and "Paused" for a person.</summary>
    public static string ReasonOf(ChannelDownReason reason, string? errorName) =>
        reason == ChannelDownReason.Operator ? "Paused" : errorName ?? "Errors";

    /// <summary>
    /// Gives the start of the current state of a channel in UTC and the time since then, with the previous state and its
    /// duration (D279), for example "2026-11-18 13:32 UTC (3 d 4 h), after Down for 4 min", or "No down state recorded" for a
    /// channel with no state row.
    /// </summary>
    public static string SinceOf(RegistryChannel channel, DateTime nowUtc) =>
        channel.Current is not { } current ? "No down state recorded"
        : current is { PreviousKind: { } previousKind, PreviousDuration: { } previousDuration }
            ? $"{TimeOf(current.SinceUtc)} ({DurationOf(nowUtc - current.SinceUtc)}), after {previousKind} for {DurationOf(previousDuration)}"
            : $"{TimeOf(current.SinceUtc)} ({DurationOf(nowUtc - current.SinceUtc)})";

    /// <summary>Gives the previous state of a row and its duration, for example "Down for 4 min", or nothing for the version 1.</summary>
    public static string PreviousOf(ChannelState row) =>
        row is { PreviousKind: { } kind, PreviousDuration: { } duration } ? $"{kind} for {DurationOf(duration)}" : string.Empty;

    /// <summary>Gives a UTC time to the minute, for example "2026-11-18 13:32 UTC".</summary>
    public static string TimeOf(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Gives a duration in days and hours, in hours and minutes, or in minutes. A negative duration is 0.</summary>
    public static string DurationOf(TimeSpan duration)
    {
        var positive = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return positive.TotalDays >= 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)positive.TotalDays} d {positive.Hours} h")
            : positive.TotalHours >= 1 ? string.Create(CultureInfo.InvariantCulture, $"{positive.Hours} h {positive.Minutes} min")
            : string.Create(CultureInfo.InvariantCulture, $"{positive.Minutes} min");
    }
}
