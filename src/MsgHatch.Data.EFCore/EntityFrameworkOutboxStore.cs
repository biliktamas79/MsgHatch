using System.Text;
using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore.Messages;
using MsgHatch.Data.EFCore.Model;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The outbox on EF Core (section 4.5, D120, D318): one row of the table <c>Outbox</c> for each outgoing message,
/// with the columns of its delivery state, its channel envelope and its route trace as JSON (D143), and its publish tries
/// as the JSON array <c>Tries</c> (D27), which the store writes in C#. The table <c>ChannelState</c> has the
/// insert-only state rows of each channel, with the key <c>(Channel, Version)</c> (D106, D277).
/// Each operation makes its own context through the pooled factory (D130). It is the dispatch store of all tenants,
/// and <see cref="For"/> gives the tenant store of one key.
/// </summary>
public sealed class EntityFrameworkOutboxStore : IOutboxDispatchStore
{
    private const string _test = nameof(MessageFlags.Test);

    private readonly StoreContexts<MsgHatchDbContext> _contexts;
    private readonly MsgHatchJson _json;

    /// <summary>Makes the store on the contexts of a factory.</summary>
    /// <param name="contexts">The pooled factory of the contexts.</param>
    /// <param name="json">The serializer of the messages.</param>
    /// <param name="classifiers">
    /// The exception classifiers of the host (D141, D322). The store asks them before the dialect and its built-in mapping.
    /// Null means none.
    /// </param>
    public EntityFrameworkOutboxStore(
        IDbContextFactory<MsgHatchDbContext> contexts, MsgHatchJson json, IEnumerable<IExceptionClassifier<MsgHatchDbContext>>? classifiers = null)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(json);
        _contexts = new StoreContexts<MsgHatchDbContext>(contexts, classifiers);
        _json = json;
    }

    /// <summary>Gives the tenant store of one key. It reads and writes only the messages of its tenant.</summary>
    /// <param name="key">The tenant key.</param>
    /// <returns>The tenant store.</returns>
    public IOutboxStore For(TenantKey key) => new TenantStore(this, key);

    /// <inheritdoc />
    public async ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimDueAsync(
        ChannelId channel, int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : await _contexts.RunAsync(
                async context =>
                {
                    if ((await LatestStateAsync(context, channel, ct))?.ToDown() is { } down)
                    {
                        return new ClaimResult<OutgoingMessage>([], NextDueAtUtc: null, down);
                    }

                    return await MessageQueries.ClaimAsync(
                        context, context.Outbox, context.Outbox.Where(row => row.Channel == channel.Name), channel.Name,
                        new ClaimArguments(maxCount, nowUtc, nowUtc + lease), row => _json.DeserializeOutgoing(MessageDocuments.Outgoing(row)), ct);
                },
                ct);

    /// <inheritdoc />
    public ValueTask<Error?> RecordTryAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publishTry);
        var id = MessageQueries.Id(message.Delivery.Id);
        return _contexts.RunAsync(
            async context =>
            {
                while (await context.Outbox.AsNoTracking().Where(row => row.Id == id)
                    .Select(static row => new StoredTries(row.TryCount, row.Tries)).FirstOrDefaultAsync(ct) is { } stored)
                {
                    if (stored.TryCount != publishTry.TryNumber - 1
                        || await TryRecordAsync(context.Outbox.Where(row => row.Id == id), stored, publishTry, nextTryAtUtc, ct))
                    {
                        return null;
                    }
                }

                return new Error(Errors.NotFound, "The outgoing message is not stored.");
            },
            ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) => Count(tenant: null, query, ct);

    /// <inheritdoc />
    public async ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : await _contexts.RunAsync<int>(async context => await MessageQueries.PurgeAsync(context.Outbox, finishedBeforeUtc, maxCount, ct), ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        _contexts.RunAsync<int>(async context => await MessageQueries.DeleteTestDataAsync(context.Outbox, ct), ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct) =>
        _contexts.RunAsync<IReadOnlyList<ChannelDown>>(
            async context =>
            {
                var rows = await context.ChannelStates.AsNoTracking()
                    .Where(row => row.Kind == nameof(ChannelStateKind.Down)
                        && row.Version == context.ChannelStates.Where(other => other.Channel == row.Channel).Max(static other => other.Version))
                    .ToListAsync(ct);
                IReadOnlyList<ChannelDown> downs = [.. rows.Select(static row => StateOf(row).ToDown()!)];
                return ErrorOr<IReadOnlyList<ChannelDown>>.ForValue(downs);
            },
            ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(down);
        return _contexts.RunAsync(
            async context => await ChannelState.ChangeAsync(
                async () => await LatestStateAsync(context, down.Channel, ct), latest => ChannelState.AfterDown(latest, down),
                async row => await TryAddStateAsync(context, row, ct)),
            ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelStatesAsync(DateTime fromUtc, CancellationToken ct) =>
        _contexts.RunAsync<IReadOnlyList<ChannelState>>(
            async context =>
            {
                var rows = await context.ChannelStates.AsNoTracking()
                    .Where(row => row.SinceUtc >= fromUtc || row.Version == context.ChannelStates
                        .Where(other => other.Channel == row.Channel && other.SinceUtc < fromUtc).Max(static other => (int?)other.Version))
                    .OrderByDescending(static row => row.SinceUtc).ThenBy(static row => row.Channel).ThenByDescending(static row => row.Version)
                    .ToListAsync(ct);
                IReadOnlyList<ChannelState> states = [.. rows.Select(StateOf)];
                return ErrorOr<IReadOnlyList<ChannelState>>.ForValue(states);
            },
            ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, DateTime upAtUtc, CancellationToken ct) =>
        _contexts.RunAsync(
            async context => await ChannelState.ChangeAsync(
                async () => await LatestStateAsync(context, channel, ct), latest => ChannelState.AfterUp(latest, channel, reason, upAtUtc),
                async row => await TryAddStateAsync(context, row, ct)),
            ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct) =>
        _contexts.RunAsync<bool>(
            async context => await context.ChannelStates
                .Where(row => row.Channel == channel.Name && row.Kind == nameof(ChannelStateKind.Down)
                    && row.Reason == nameof(ChannelDownReason.Errors) && row.NextCheckAtUtc <= nowUtc
                    && row.Version == context.ChannelStates.Where(other => other.Channel == channel.Name).Max(static other => other.Version))
                .ExecuteUpdateAsync(set => set.SetProperty(static row => row.NextCheckAtUtc, nextCheckAtUtc), ct) > 0,
            ct);

    /// <summary>
    /// Writes the try and the delivery state after it, only when the stored <c>TryCount</c> is still the count that the
    /// store read. A false result means that another writer changed the row first (section 4.5, D27).
    /// </summary>
    private static async Task<bool> TryRecordAsync(
        IQueryable<OutboxRow> row, StoredTries stored, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        var status = publishTry.StatusAfter(nextTryAtUtc);
        var tries = PublishTries.Append(stored.Tries, publishTry);
        var lastError = publishTry.ErrorText();
        return await row.Where(row => row.TryCount == stored.TryCount).ExecuteUpdateAsync(
            set =>
            {
                set.SetProperty(static row => row.Tries, tries)
                    .SetProperty(static row => row.Status, status.ToString())
                    .SetProperty(static row => row.TryCount, publishTry.TryNumber)
                    .SetProperty(static row => row.LastTryAtUtc, publishTry.FinishedAtUtc)
                    .SetProperty(static row => row.LeaseUntilUtc, (DateTime?)null)
                    .SetProperty(static row => row.LastError, lastError);
                if (status == DeliveryStatus.Pending && nextTryAtUtc is { } next)
                {
                    set.SetProperty(static row => row.NextTryAtUtc, next);
                }
            },
            ct) > 0;
    }

    private static async Task<ChannelState?> LatestStateAsync(MsgHatchDbContext context, ChannelId channel, CancellationToken ct) =>
        await context.ChannelStates.AsNoTracking().Where(row => row.Channel == channel.Name).OrderByDescending(static row => row.Version)
            .FirstOrDefaultAsync(ct) is { } row ? StateOf(row) : null;

    /// <summary>Inserts a state row on the key of the channel and the version. A stored version keeps its row, and gives false (D277).</summary>
    private static Task<bool> TryAddStateAsync(MsgHatchDbContext context, ChannelState state, CancellationToken ct) =>
        RowWrites.TryAddAsync(
            context,
            new ChannelStateRow
            {
                Channel = state.Channel.Name, Version = state.Version, Kind = state.Kind.ToString(), SinceUtc = state.SinceUtc,
                PreviousKind = state.PreviousKind?.ToString(), PreviousDurationMs = (long?)state.PreviousDuration?.TotalMilliseconds,
                Reason = state.Reason?.ToString(), NextCheckAtUtc = state.NextCheckAtUtc, ErrorCode = state.ErrorCode, ErrorName = state.ErrorName,
                Note = state.Note,
            },
            () => context.ChannelStates.AnyAsync(row => row.Channel == state.Channel.Name && row.Version == state.Version, ct),
            ct);

    /// <exception cref="ArgumentException">The row has an invalid channel name, kind, or reason.</exception>
    private static ChannelState StateOf(ChannelStateRow row) =>
        new(
            new ChannelId(row.Channel), row.Version, Enum.Parse<ChannelStateKind>(row.Kind), row.SinceUtc,
            row.PreviousKind is { } previousKind ? Enum.Parse<ChannelStateKind>(previousKind) : null,
            row.PreviousDurationMs is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : null,
            row.Reason is { } reason ? Enum.Parse<ChannelDownReason>(reason) : null, row.NextCheckAtUtc, row.ErrorCode, row.ErrorName, row.Note);

    private async ValueTask<Error?> AddAsync(TenantKey key, IReadOnlyList<OutgoingMessage> messages, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Any(message => message.Tenant != key.Tenant))
        {
            return new Error(Errors.ValidationFailed, "A message has another tenant than the key of the store.");
        }

        var documents = new List<(OutgoingMessage Message, byte[] Document)>(messages.Count);
        foreach (var message in messages)
        {
            var document = _json.Serialize(message);
            if (document.IsError)
            {
                return document.Error;
            }

            documents.Add((message, document.Value));
        }

        return await _contexts.RunAsync(
            async context =>
            {
                await AddWithNoStoredIdAsync(
                    context, [.. documents.Select(static item => MessageRows.Outgoing(item.Message, item.Document)).DistinctBy(static row => row.Id)], ct);
                return (Error?)null;
            },
            ct);
    }

    /// <summary>
    /// Adds the rows in one transaction, in the order of the batch, so their <c>Sequence</c> keeps that order (D144). The
    /// rows have different ids. When a row has a stored id, the store adds the rows with no stored id in a new transaction,
    /// because some databases, for example PostgreSQL, end a transaction after an error (D318).
    /// </summary>
    private static Task AddWithNoStoredIdAsync(MsgHatchDbContext context, IReadOnlyList<OutboxRow> rows, CancellationToken ct) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var missing = rows;
            while (missing.Count > 0)
            {
                try
                {
                    await AddInOneTransactionAsync(context, missing, ct);
                    return;
                }
                catch (DbUpdateException)
                {
                    context.ChangeTracker.Clear();
                    var stillMissing = await WithNoStoredIdAsync(context, missing, ct);
                    if (stillMissing.Count == missing.Count)
                    {
                        throw;
                    }

                    missing = stillMissing;
                }
            }
        });

    /// <summary>Inserts the rows one by one in one transaction. A row of a failed transaction gets a new <c>Sequence</c>.</summary>
    private static async Task AddInOneTransactionAsync(MsgHatchDbContext context, IReadOnlyList<OutboxRow> rows, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        foreach (var row in rows)
        {
            row.Sequence = default;
            context.Outbox.Add(row);
            await context.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        context.ChangeTracker.Clear();
    }

    private static async Task<IReadOnlyList<OutboxRow>> WithNoStoredIdAsync(MsgHatchDbContext context, IReadOnlyList<OutboxRow> rows, CancellationToken ct)
    {
        string[] ids = [.. rows.Select(static row => row.Id)];
        var stored = await context.Outbox.Where(row => ids.Contains(row.Id)).Select(static row => row.Id).ToListAsync(ct);
        return [.. rows.Where(row => !stored.Contains(row.Id))];
    }

    private async ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(TenantKey key, OutboxQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _contexts.RunAsync<MessagePage<OutgoingMessageSummary>>(
            async context =>
            {
                var filtered = MessageQueries.Filter(
                    context.Outbox, key, query.Ids, query.Status, query.CreatedFromUtc, query.CreatedToUtc, query.DueBeforeUtc, query.IncludeTest, query.After);
                if (query.Type is { } type)
                {
                    filtered = filtered.Where(row => row.MessageType == type.Name);
                }

                if (query.Channel is { } channel)
                {
                    filtered = filtered.Where(row => row.Channel == channel.Name);
                }

                if (query.Target is { } target)
                {
                    var kind = target.Kind.ToString();
                    filtered = filtered.Where(row => row.TargetKind == kind && row.TargetId == target.Id);
                }

                if (query.SourceMessageId is { } source)
                {
                    var sourceId = MessageQueries.Id(source);
                    filtered = filtered.Where(row => row.SourceMessageId == sourceId);
                }

                return await MessageQueries.PageAsync(
                    filtered.Select(static row => new OutboxRow
                    {
                        Sequence = row.Sequence, TenantId = row.TenantId, Id = row.Id, Status = row.Status, TryCount = row.TryCount,
                        MaxTryCount = row.MaxTryCount, CreatedAtUtc = row.CreatedAtUtc, LastTryAtUtc = row.LastTryAtUtc,
                        NextTryAtUtc = row.NextTryAtUtc, LeaseUntilUtc = row.LeaseUntilUtc, ExpiresAtUtc = row.ExpiresAtUtc,
                        LastError = row.LastError, SourceMessageId = row.SourceMessageId, MessageType = row.MessageType, Channel = row.Channel,
                        TargetKind = row.TargetKind, TargetId = row.TargetId, ContentType = row.ContentType,
                        ContentVersion = row.ContentVersion, Flags = row.Flags,
                    }),
                    query.Limit, SummaryOf, static summary => summary.Delivery, ct);
            },
            ct);
    }

    private ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(TenantKey key, Guid id, CancellationToken ct)
    {
        var tenant = MessageQueries.Tenant(key.Tenant);
        var text = MessageQueries.Id(id);
        return _contexts.RunAsync<OutgoingMessageHistory?>(
            async context =>
            {
                if (await context.Outbox.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenant && row.Id == text, ct) is not { } row)
                {
                    return ErrorOr<OutgoingMessageHistory?>.ForValue(null);
                }

                var message = _json.DeserializeOutgoing(MessageDocuments.Outgoing(row));
                var tries = MsgHatchJson.DeserializeTries(id, Encoding.UTF8.GetBytes(row.Tries));
                return message.Error ?? tries.Error ?? (ErrorOr<OutgoingMessageHistory?>)new OutgoingMessageHistory(message.Value, tries.Value);
            },
            ct);
    }

    /// <param name="tenant">The key of a tenant store, or null for all tenants.</param>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancel token.</param>
    private ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> Count(TenantKey? tenant, TrafficQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenantId = tenant is { } key ? MessageQueries.Tenant(key.Tenant) : null;
        var status = query.Status?.ToString();
        return _contexts.RunAsync<IReadOnlyList<MessageCount>>(
            async context =>
            {
                var rows = context.Outbox.AsQueryable();
                rows = tenantId is null ? rows : rows.Where(row => row.TenantId == tenantId);
                rows = query.CreatedFromUtc is { } from ? rows.Where(row => row.CreatedAtUtc >= from) : rows;
                rows = query.CreatedToUtc is { } to ? rows.Where(row => row.CreatedAtUtc < to) : rows;
                rows = status is null ? rows : rows.Where(row => row.Status == status);
                rows = query.IncludeTest ? rows : rows.Where(static row => row.Flags != _test);
                var groups = await rows
                    .GroupBy(static row => new { row.MessageType, row.Channel, row.Status, Test = row.Flags == _test })
                    .Select(static group => new
                    {
                        group.Key.MessageType,
                        group.Key.Channel,
                        group.Key.Status,
                        group.Key.Test,
                        Count = group.Count(),
                        OldestDueAtUtc = group.Min(static row => row.Status == MessageQueries.Pending ? (DateTime?)row.NextTryAtUtc : null),
                    })
                    .ToListAsync(ct);
                IReadOnlyList<MessageCount> counts = [.. groups.Select(static group => new MessageCount(
                    new MessageType(group.MessageType), new ChannelId(group.Channel), Enum.Parse<DeliveryStatus>(group.Status), group.Test,
                    group.Count, group.OldestDueAtUtc))];
                return ErrorOr<IReadOnlyList<MessageCount>>.ForValue(counts);
            },
            ct);
    }

    private ValueTask<ErrorOr<int>> CancelAsync(TenantKey key, IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return _contexts.RunAsync<int>(
            async context =>
            {
                var cancelled = 0;
                foreach (var id in ids)
                {
                    cancelled += await TryCancelAsync(MessageQueries.Cancellable(context.Outbox, key, [id], nowUtc), id, nowUtc, ct) ? 1 : 0;
                }

                return cancelled;
            },
            ct);
    }

    /// <summary>
    /// Cancels the message while it is cancellable, and records the cancelled try with the next try number (D110). The
    /// write has the condition of the <c>TryCount</c> that the store read, so a change of another writer makes the store
    /// read the row again.
    /// </summary>
    private static async Task<bool> TryCancelAsync(IQueryable<OutboxRow> cancellable, Guid id, DateTime nowUtc, CancellationToken ct)
    {
        while (await cancellable.AsNoTracking().Select(static row => new StoredTries(row.TryCount, row.Tries)).FirstOrDefaultAsync(ct) is { } stored)
        {
            var tryCount = stored.TryCount + 1;
            var tries = PublishTries.Append(stored.Tries, PublishTry.Cancel(id, tryCount, nowUtc));
            var changed = await cancellable.Where(row => row.TryCount == stored.TryCount).ExecuteUpdateAsync(
                set => set
                    .SetProperty(static row => row.Status, nameof(DeliveryStatus.Cancelled))
                    .SetProperty(static row => row.LeaseUntilUtc, (DateTime?)null)
                    .SetProperty(static row => row.TryCount, tryCount)
                    .SetProperty(static row => row.LastTryAtUtc, nowUtc)
                    .SetProperty(static row => row.Tries, tries),
                ct);
            if (changed > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static OutgoingMessageSummary SummaryOf(OutboxRow row) =>
        new(
            MessageQueries.DeliveryOf(row), Guid.Parse(row.SourceMessageId), MessageQueries.TenantOf(row.TenantId), new MessageType(row.MessageType),
            new ChannelId(row.Channel), new DeliveryTarget(Enum.Parse<DeliveryTargetKind>(row.TargetKind), row.TargetId), row.ContentType,
            row.ContentVersion, Enum.Parse<MessageFlags>(row.Flags));

    private sealed record StoredTries(int TryCount, string Tries);

    private sealed class TenantStore(EntityFrameworkOutboxStore store, TenantKey key) : IOutboxStore
    {
        public ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct) => store.AddAsync(key, messages, ct);

        public ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct) =>
            store.QueryAsync(key, query, ct);

        public ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct) => store.GetAsync(key, id, ct);

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) => store.Count(key, query, ct);

        public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
            store._contexts.RunAsync<int>(async context => await MessageQueries.RetryAsync(context.Outbox, key, ids, nowUtc, addedTries, ct), ct);

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            store.CancelAsync(key, ids, nowUtc, ct);
    }
}
