using System.Globalization;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AdminUI;

/// <summary>The texts of the registry pages (D272, D273, D279, D283, D287).</summary>
internal static class RegistryTexts
{
    /// <summary>The help text under the enabled flag of an edit page (D287).</summary>
    public const string ChangeHelp =
        "A change applies to the next events. The outbox still sends the messages that it holds. "
        + "Another process applies the change within its refresh interval.";

    /// <summary>The placeholder of a text field with a stored value (D287).</summary>
    public const string CodeValuePlaceholder = "An empty field gives the value of the code.";

    /// <summary>The text of a successful save on an edit page (D287).</summary>
    public const string SavedText = "Saved.";

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

    /// <summary>
    /// Gives the result of a channel check (D283): "healthy" or the error name of the check, and the new state or "The state
    /// did not change.", for example "Email: Unavailable. The channel is down now.".
    /// </summary>
    public static string CheckTextOf(ChannelCheck check) =>
        $"{check.Channel.Name}: {(check.Healthy ? "healthy" : check.ErrorName)}. "
        + (!check.Changed ? "The state did not change." : check.Healthy ? "The channel is up again." : "The channel is down now.");

    /// <summary>Gives the Bootstrap class of the alert of a check: <c>alert-success</c> with no error, else <c>alert-warning</c>.</summary>
    public static string CheckClassOf(ChannelCheck check) => check.Healthy ? "alert-success" : "alert-warning";

    /// <summary>
    /// Gives the placeholder of a text field of an edit page (D287): with no stored value, the value of the query, which is
    /// then the value of the code, and with a stored value, what an empty field gives.
    /// </summary>
    public static string PlaceholderOf(string? stored, string? valueOfTheQuery) =>
        stored is null ? valueOfTheQuery ?? string.Empty : CodeValuePlaceholder;

    /// <summary>Gives the value of a text field to save: null for an empty field, which is the value of the code (D287).</summary>
    public static string? StoredValueOf(string text) => string.IsNullOrWhiteSpace(text) ? null : text;

    /// <summary>Gives a stored text of a reset question, or "Value of the code" for no stored text (D287).</summary>
    public static string StoredTextOf(string? stored) => stored ?? "Value of the code";

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
