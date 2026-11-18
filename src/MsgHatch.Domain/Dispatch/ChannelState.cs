using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// One state of a channel in the insert-only state rows of the outbox dispatch store (D277). A store adds a row only when the
/// state of the channel changes, and the latest row of a channel is its current state. A channel with no row is up. All times
/// are UTC.
/// </summary>
/// <param name="Channel">The channel.</param>
/// <param name="Version">The version of the row: 1 for the first row of the channel, and the version of the previous row plus 1.</param>
/// <param name="Kind">Up or down.</param>
/// <param name="SinceUtc">The start of the state.</param>
/// <param name="PreviousKind">The kind of the previous row of the channel, or null for the version 1.</param>
/// <param name="PreviousDuration">
/// The time since the start of the previous row of the channel, in whole milliseconds, 0 for a negative difference, or null
/// for the version 1.
/// </param>
/// <param name="Reason">Why the channel is down, only for <see cref="ChannelStateKind.Down"/>.</param>
/// <param name="NextCheckAtUtc">The time of the next health check, only for the reason <see cref="ChannelDownReason.Errors"/>.</param>
/// <param name="ErrorCode">The code of the last transient error, only for the reason <see cref="ChannelDownReason.Errors"/>.</param>
/// <param name="ErrorName">The name of the last transient error, only for the reason <see cref="ChannelDownReason.Errors"/>.</param>
/// <param name="Note">The text of the person, only for the reason <see cref="ChannelDownReason.Operator"/>.</param>
public sealed record ChannelState(
    ChannelId Channel, int Version, ChannelStateKind Kind, DateTime SinceUtc, ChannelStateKind? PreviousKind, TimeSpan? PreviousDuration,
    ChannelDownReason? Reason, DateTime? NextCheckAtUtc, int? ErrorCode, string? ErrorName, string? Note)
{
    /// <summary>The window of the channel history of the registry queries and the admin pages (D278, D279).</summary>
    public static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(30);

    /// <summary>The number of tries of a change when other callers add the same version first.</summary>
    public const int ChangeTries = 3;

    /// <summary>Gives the down state of a <see cref="ChannelStateKind.Down"/> row.</summary>
    /// <returns>The down state, or null for an <see cref="ChannelStateKind.Up"/> row.</returns>
    public ChannelDown? ToDown() =>
        Kind == ChannelStateKind.Down && Reason is { } reason
            ? new ChannelDown(Channel, reason, SinceUtc, NextCheckAtUtc, ErrorCode, ErrorName, Note)
            : null;

    /// <summary>
    /// Gives the row that a down state adds after the latest row: with the reason <see cref="ChannelDownReason.Errors"/> only on
    /// an up channel, so the first loop that finds the errors wins, and with the reason <see cref="ChannelDownReason.Operator"/>
    /// unless the channel is paused.
    /// </summary>
    /// <param name="latest">The latest row of the channel, or null for no row.</param>
    /// <param name="down">The down state.</param>
    /// <returns>The row to add, or null when the state does not change.</returns>
    public static ChannelState? AfterDown(ChannelState? latest, ChannelDown down)
    {
        ArgumentNullException.ThrowIfNull(down);
        var unchanged = latest is { Kind: ChannelStateKind.Down }
            && (down.Reason == ChannelDownReason.Errors || latest.Reason == ChannelDownReason.Operator);
        return unchanged ? null : new ChannelState(
            down.Channel, VersionAfter(latest), ChannelStateKind.Down, down.SinceUtc, latest?.Kind, DurationSince(latest, down.SinceUtc),
            down.Reason, down.NextCheckAtUtc, down.ErrorCode, down.ErrorName, down.Note);
    }

    /// <summary>Gives the row that an up change adds after the latest row: only on a down channel with the given reason.</summary>
    /// <param name="latest">The latest row of the channel, or null for no row.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="reason">The reason to end, or null for each reason.</param>
    /// <param name="upAtUtc">The up time.</param>
    /// <returns>The row to add, or null when the state does not change.</returns>
    public static ChannelState? AfterUp(ChannelState? latest, ChannelId channel, ChannelDownReason? reason, DateTime upAtUtc) =>
        latest is { Kind: ChannelStateKind.Down } && (reason is null || latest.Reason == reason)
            ? new ChannelState(
                channel, VersionAfter(latest), ChannelStateKind.Up, upAtUtc, latest.Kind, DurationSince(latest, upAtUtc), null, null, null, null, null)
            : null;

    /// <summary>
    /// Runs a change of a store that reads and inserts with no exception boundary of its own: it reads the latest row, and
    /// inserts the next row of <paramref name="next"/>. When another caller added the same version first, it tries again, at
    /// most <see cref="ChangeTries"/> times.
    /// </summary>
    /// <param name="readLatest">Reads the latest row of the channel, or null for no row.</param>
    /// <param name="next">Gives the row to add after the latest row, or null for no change.</param>
    /// <param name="tryInsert">Inserts the row, and gives false when the store has its version.</param>
    /// <returns>True when it added a row, false for no change, or <see cref="Errors.Conflict"/> after the last try.</returns>
    public static ErrorOr<bool> Change(Func<ChannelState?> readLatest, Func<ChannelState?, ChannelState?> next, Func<ChannelState, bool> tryInsert)
    {
        ArgumentNullException.ThrowIfNull(readLatest);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(tryInsert);
        for (var attempt = 0; attempt < ChangeTries; attempt++)
        {
            if (next(readLatest()) is not { } row)
            {
                return false;
            }

            if (tryInsert(row))
            {
                return true;
            }
        }

        return LostEachTry();
    }

    /// <summary>The asynchronous form of <see cref="Change"/>.</summary>
    /// <param name="readLatest">Reads the latest row of the channel, or null for no row.</param>
    /// <param name="next">Gives the row to add after the latest row, or null for no change.</param>
    /// <param name="tryInsert">Inserts the row, and gives false when the store has its version.</param>
    /// <returns>True when it added a row, false for no change, or <see cref="Errors.Conflict"/> after the last try.</returns>
    public static async ValueTask<ErrorOr<bool>> ChangeAsync(
        Func<ValueTask<ChannelState?>> readLatest, Func<ChannelState?, ChannelState?> next, Func<ChannelState, ValueTask<bool>> tryInsert)
    {
        ArgumentNullException.ThrowIfNull(readLatest);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(tryInsert);
        for (var attempt = 0; attempt < ChangeTries; attempt++)
        {
            if (next(await readLatest()) is not { } row)
            {
                return false;
            }

            if (await tryInsert(row))
            {
                return true;
            }
        }

        return LostEachTry();
    }

    /// <summary>
    /// Gives the rows at or after <paramref name="fromUtc"/>, and the newest row before it of each channel, in the order of
    /// <see cref="Outbox.IOutboxDispatchStore.GetChannelStatesAsync"/>, for a store that reads the rows into memory.
    /// </summary>
    /// <param name="rows">The stored rows.</param>
    /// <param name="fromUtc">The earliest start of a row, besides the row that was current at that time.</param>
    /// <returns>The rows, newest first.</returns>
    public static IReadOnlyList<ChannelState> NewestFirst(IEnumerable<ChannelState> rows, DateTime fromUtc)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var stored = rows as IReadOnlyCollection<ChannelState> ?? [.. rows];
        var currentAtFrom = stored
            .Where(row => row.SinceUtc < fromUtc)
            .GroupBy(static row => row.Channel)
            .Select(static group => group.MaxBy(static row => row.Version)!);
        return
        [
            .. stored.Where(row => row.SinceUtc >= fromUtc).Concat(currentAtFrom)
                .OrderByDescending(static row => row.SinceUtc)
                .ThenBy(static row => row.Channel.Name, StringComparer.Ordinal)
                .ThenByDescending(static row => row.Version),
        ];
    }

    private static int VersionAfter(ChannelState? latest) => (latest?.Version ?? 0) + 1;

    private static TimeSpan? DurationSince(ChannelState? latest, DateTime sinceUtc) =>
        latest is null ? null : TimeSpan.FromMilliseconds(Math.Max(0, Math.Floor((sinceUtc - latest.SinceUtc).TotalMilliseconds)));

    private static Error LostEachTry() =>
        new(Errors.Conflict, $"Other callers changed the state of the channel first in each of {ChangeTries} tries.");
}
