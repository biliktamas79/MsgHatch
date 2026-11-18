using System.Globalization;
using MsgHatch.Domain.Dispatch;

namespace MsgHatch.AdminUI;

/// <summary>One part of the timeline bar of the channel history: down or up, and its part of the window in percent.</summary>
/// <param name="Down">True where the channel was down.</param>
/// <param name="Percent">The part of the window.</param>
internal sealed record TimelineSegment(bool Down, double Percent)
{
    /// <summary>The percent as invariant text with at most three decimals, for the width of the bar.</summary>
    public string PercentText => Percent.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>The timeline bar of the channel history (D279): the down and up parts of the window of 30 days, oldest first.</summary>
internal static class ChannelTimeline
{
    /// <summary>
    /// Gives the parts of the window that ends now. The newest row at or before the start of the window gives the state at the
    /// start of the bar. With no such row, the channel was up.
    /// </summary>
    /// <param name="newestFirst">The state rows of one channel, newest first.</param>
    /// <param name="nowUtc">The end of the window.</param>
    /// <returns>The parts, oldest first, with no two neighbor parts of the same state.</returns>
    public static IReadOnlyList<TimelineSegment> Of(IReadOnlyList<ChannelState> newestFirst, DateTime nowUtc)
    {
        var windowStartUtc = nowUtc - ChannelState.HistoryWindow;
        ChannelState[] oldestFirst = [.. newestFirst.Where(row => row.SinceUtc > windowStartUtc && row.SinceUtc <= nowUtc).Reverse()];
        var down = newestFirst.FirstOrDefault(row => row.SinceUtc <= windowStartUtc) is { Kind: ChannelStateKind.Down };
        var segments = new List<TimelineSegment>();
        var fromUtc = windowStartUtc;
        foreach (var row in oldestFirst)
        {
            Add(segments, down, row.SinceUtc - fromUtc);
            (down, fromUtc) = (row.Kind == ChannelStateKind.Down, row.SinceUtc);
        }

        Add(segments, down, nowUtc - fromUtc);
        return segments;
    }

    private static void Add(List<TimelineSegment> segments, bool down, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var percent = duration / ChannelState.HistoryWindow * 100;
        if (segments.Count > 0 && segments[^1].Down == down)
        {
            segments[^1] = segments[^1] with { Percent = segments[^1].Percent + percent };
        }
        else
        {
            segments.Add(new TimelineSegment(down, percent));
        }
    }
}
