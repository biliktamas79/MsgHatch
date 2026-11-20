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

/// <summary>
/// The timeline bar of the channel history (D279): the down and up parts of the window of 30 days, oldest first, and the up
/// time of a window of at most 30 days.
/// </summary>
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
        var segments = new List<TimelineSegment>();
        foreach (var (down, duration) in PartsOf(newestFirst, nowUtc, ChannelState.HistoryWindow))
        {
            Add(segments, down, duration / ChannelState.HistoryWindow * 100);
        }

        return segments;
    }

    /// <summary>
    /// Gives the part of a window that ends now in which the channel was up, in percent. The rows of the history query hold the
    /// window of 30 days, so the window is at most 30 days.
    /// </summary>
    /// <param name="newestFirst">The state rows of one channel, newest first.</param>
    /// <param name="nowUtc">The end of the window.</param>
    /// <param name="window">The length of the window.</param>
    /// <returns>The up time from 0 to 100, counted in ticks.</returns>
    public static decimal UpPercentOf(IReadOnlyList<ChannelState> newestFirst, DateTime nowUtc, TimeSpan window)
    {
        var downTicks = PartsOf(newestFirst, nowUtc, window).Where(static part => part.Down).Sum(static part => part.Duration.Ticks);
        return (decimal)(window.Ticks - downTicks) / window.Ticks * 100;
    }

    private static IEnumerable<(bool Down, TimeSpan Duration)> PartsOf(IReadOnlyList<ChannelState> newestFirst, DateTime nowUtc, TimeSpan window)
    {
        var windowStartUtc = nowUtc - window;
        ChannelState[] oldestFirst = [.. newestFirst.Where(row => row.SinceUtc > windowStartUtc && row.SinceUtc <= nowUtc).Reverse()];
        var down = newestFirst.FirstOrDefault(row => row.SinceUtc <= windowStartUtc) is { Kind: ChannelStateKind.Down };
        var fromUtc = windowStartUtc;
        foreach (var row in oldestFirst)
        {
            yield return (down, row.SinceUtc - fromUtc);
            (down, fromUtc) = (row.Kind == ChannelStateKind.Down, row.SinceUtc);
        }

        yield return (down, nowUtc - fromUtc);
    }

    private static void Add(List<TimelineSegment> segments, bool down, double percent)
    {
        if (percent <= 0)
        {
            return;
        }

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
