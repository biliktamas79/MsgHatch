using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Data.EFCore.Messages;

/// <summary>
/// The operations that the inbox and the outbox share: the claim (D103, D144, D318), the filters and the page of a query
/// (D112), the retry (D111), the purge (D128), and the values of a row.
/// </summary>
internal static class MessageQueries
{
    public const string Pending = nameof(DeliveryStatus.Pending);

    private const string _failed = nameof(DeliveryStatus.Failed);

    private const string _test = nameof(MessageFlags.Test);

    public static string Id(Guid id) => id.ToString("D");

    public static string Tenant(TenantId? tenant) => tenant?.Value ?? string.Empty;

    public static TenantId? TenantOf(string text) => text.Length == 0 ? null : new TenantId(text);

    /// <exception cref="FormatException">The row has an invalid id.</exception>
    /// <exception cref="ArgumentException">The row has an invalid status.</exception>
    public static DeliveryState DeliveryOf(MessageRow row) =>
        new(
            Guid.Parse(row.Id), Enum.Parse<DeliveryStatus>(row.Status), row.TryCount, row.MaxTryCount, row.CreatedAtUtc, row.LastTryAtUtc,
            row.NextTryAtUtc, row.LeaseUntilUtc, row.ExpiresAtUtc, row.LastError);

    /// <summary>
    /// Claims the due rows (D318), and returns their messages in the claim order: the due time before the lease, then
    /// <c>Sequence</c> (D144). The end of the lease is the next due time of a claimed row (D103). A row that the code
    /// cannot read stays out of the result: a transient error keeps it leased for a newer instance (section 4.13), and a
    /// permanent error fails it.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="table">The message table.</param>
    /// <param name="rows">The rows that the claim can take: each row of the inbox, or the rows of one channel of the outbox.</param>
    /// <param name="channel">The channel of the outbox claim, or null for the inbox.</param>
    /// <param name="claim">The values of the claim.</param>
    /// <param name="read">Reads the message of a claimed row.</param>
    /// <param name="ct">The cancel token.</param>
    public static async ValueTask<ErrorOr<ClaimResult<T>>> ClaimAsync<TRow, T>(
        DbContext context, DbSet<TRow> table, IQueryable<TRow> rows, string? channel, ClaimArguments claim, Func<TRow, ErrorOr<T>> read,
        CancellationToken ct)
        where TRow : MessageRow
    {
        var claimed = await MessageClaims.ClaimAsync(context, table, rows, channel, claim, ct);
        var messages = new List<T>(claimed.Count);
        foreach (var row in claimed.OrderBy(static row => row.ClaimedDueAtUtc).ThenBy(static row => row.Sequence))
        {
            var message = read(row);
            if (message.IsError)
            {
                await SetAsideAsync(rows, row.Sequence, message.Error!, ct);
                continue;
            }

            messages.Add(message.Value);
        }

        var nextDueAtUtc = claimed.Count < claim.MaxCount ? await NextDueAsync(rows, ct) : claim.NowUtc;
        return new ClaimResult<T>(messages, nextDueAtUtc);
    }

    public static Task<DateTime?> NextDueAsync<TRow>(IQueryable<TRow> rows, CancellationToken ct)
        where TRow : MessageRow =>
        rows.Where(static row => row.Status == Pending).MinAsync(static row => (DateTime?)row.NextTryAtUtc, ct);

    /// <summary>Filters the rows of a query with the filters that the inbox and the outbox share, and with the cursor of the page (D112).</summary>
    [SuppressMessage("Globalization", "CA1309", Justification = "EF Core translates string.Compare to SQL, where the binary collation compares ordinally.")]
    public static IQueryable<TRow> Filter<TRow>(
        IQueryable<TRow> rows, TenantKey key, IReadOnlyList<Guid>? ids, DeliveryStatus? status, DateTime? createdFromUtc,
        DateTime? createdToUtc, DateTime? dueBeforeUtc, bool includeTest, MessageCursor? after)
        where TRow : MessageRow
    {
        var tenant = Tenant(key.Tenant);
        var filtered = rows.Where(row => row.TenantId == tenant);
        if (ids is not null)
        {
            string[] texts = [.. ids.Select(Id)];
            filtered = filtered.Where(row => texts.Contains(row.Id));
        }

        if (status is { } value)
        {
            var text = value.ToString();
            filtered = filtered.Where(row => row.Status == text);
        }

        if (createdFromUtc is { } from)
        {
            filtered = filtered.Where(row => row.CreatedAtUtc >= from);
        }

        if (createdToUtc is { } to)
        {
            filtered = filtered.Where(row => row.CreatedAtUtc < to);
        }

        if (dueBeforeUtc is { } dueBefore)
        {
            filtered = filtered.Where(row => row.Status == Pending && row.NextTryAtUtc < dueBefore);
        }

        if (!includeTest)
        {
            filtered = filtered.Where(static row => row.Flags != _test);
        }

        if (after is { } cursor)
        {
            var createdAtUtc = cursor.CreatedAtUtc;
            var id = Id(cursor.Id);
            filtered = filtered.Where(row => row.CreatedAtUtc < createdAtUtc || (row.CreatedAtUtc == createdAtUtc && string.Compare(row.Id, id) < 0));
        }

        return filtered;
    }

    /// <summary>Reads one page newest first: <c>CreatedAtUtc</c> descending, then the id descending (D112).</summary>
    public static async Task<MessagePage<T>> PageAsync<TRow, T>(
        IQueryable<TRow> filtered, int limit, Func<TRow, T> summary, Func<T, DeliveryState> delivery, CancellationToken ct)
        where TRow : MessageRow
    {
        var rows = await filtered.OrderByDescending(static row => row.CreatedAtUtc).ThenByDescending(static row => row.Id).Take(limit).ToListAsync(ct);
        List<T> items = [.. rows.Select(summary)];
        return new MessagePage<T>(items, items.Count > 0 && items.Count == limit ? MessageCursor.Of(delivery(items[^1])) : null);
    }

    /// <summary>Makes the failed and the pending messages of the ids due now, when no lease is active and the message has not expired (D111).</summary>
    public static Task<int> RetryAsync<TRow>(
        IQueryable<TRow> rows, TenantKey key, IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct)
        where TRow : MessageRow
    {
        ArgumentNullException.ThrowIfNull(ids);
        var tenant = Tenant(key.Tenant);
        string[] texts = [.. ids.Select(Id)];
        return rows
            .Where(row => row.TenantId == tenant && texts.Contains(row.Id) && (row.Status == _failed || row.Status == Pending)
                && (row.LeaseUntilUtc == null || row.LeaseUntilUtc <= nowUtc) && (row.ExpiresAtUtc == null || row.ExpiresAtUtc > nowUtc))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(static row => row.Status, Pending)
                    .SetProperty(static row => row.NextTryAtUtc, nowUtc)
                    .SetProperty(static row => row.LeaseUntilUtc, (DateTime?)null)
                    .SetProperty(static row => row.MaxTryCount, row => row.Status == _failed ? row.TryCount + addedTries : row.MaxTryCount),
                ct);
    }

    /// <summary>The pending rows of the ids of one tenant with no active lease, which a cancel changes (D110).</summary>
    public static IQueryable<TRow> Cancellable<TRow>(IQueryable<TRow> rows, TenantKey key, IReadOnlyList<Guid> ids, DateTime nowUtc)
        where TRow : MessageRow
    {
        ArgumentNullException.ThrowIfNull(ids);
        var tenant = Tenant(key.Tenant);
        string[] texts = [.. ids.Select(Id)];
        return rows.Where(row => row.TenantId == tenant && texts.Contains(row.Id) && row.Status == Pending
            && (row.LeaseUntilUtc == null || row.LeaseUntilUtc <= nowUtc));
    }

    /// <summary>Deletes up to <paramref name="maxCount"/> finished rows that finished before a time, those that finished first first (D128).</summary>
    public static Task<int> PurgeAsync<TRow>(IQueryable<TRow> rows, DateTime finishedBeforeUtc, int maxCount, CancellationToken ct)
        where TRow : MessageRow =>
        rows
            .Where(row => (row.Status == nameof(DeliveryStatus.Done) || row.Status == _failed || row.Status == nameof(DeliveryStatus.Cancelled))
                && (row.LastTryAtUtc ?? row.CreatedAtUtc) < finishedBeforeUtc)
            .OrderBy(static row => row.LastTryAtUtc ?? row.CreatedAtUtc)
            .Take(maxCount)
            .ExecuteDeleteAsync(ct);

    public static Task<int> DeleteTestDataAsync<TRow>(IQueryable<TRow> rows, CancellationToken ct)
        where TRow : MessageRow =>
        rows.Where(static row => row.Flags == _test).ExecuteDeleteAsync(ct);

    public static bool IsTest(string flags) => flags == _test;

    private static async Task SetAsideAsync<TRow>(IQueryable<TRow> rows, long sequence, Error error, CancellationToken ct)
        where TRow : MessageRow
    {
        if (error.Type.IsTransient)
        {
            return;
        }

        var lastError = $"{error.Type.Name}: {error.Message}";
        await rows.Where(row => row.Sequence == sequence).ExecuteUpdateAsync(
            set => set
                .SetProperty(static row => row.Status, _failed)
                .SetProperty(static row => row.LeaseUntilUtc, (DateTime?)null)
                .SetProperty(static row => row.LastError, lastError),
            ct);
    }
}
