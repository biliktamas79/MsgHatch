using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore.Messages;
using MsgHatch.Data.EFCore.Model;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The inbox on EF Core (section 4.5, D120, D318): one row of the table <c>Inbox</c> for each received message,
/// with the columns of its delivery state, and its event as JSON in the column <c>Content</c> (D143). The key of a row is
/// the tenant and the id, so an add of a stored key changes nothing. Each operation makes its own context through the
/// pooled factory (D130), except the staged add of the tenant store of <see cref="For(TenantKey, DbContext)"/>, which adds
/// to the context of the host (D332). It is the dispatch store of all tenants, and <see cref="For(TenantKey)"/> gives the
/// tenant store of one key.
/// </summary>
public sealed class EntityFrameworkInboxStore : IInboxDispatchStore
{
    private readonly StoreContexts<MsgHatchDbContext> _contexts;
    private readonly MsgHatchJson _json;

    /// <summary>Makes the store on the contexts of a factory.</summary>
    /// <param name="contexts">The pooled factory of the contexts.</param>
    /// <param name="json">The serializer of the messages.</param>
    /// <param name="classifiers">
    /// The exception classifiers of the host (D141, D322). The store asks them before the dialect and its built-in mapping.
    /// Null means none.
    /// </param>
    public EntityFrameworkInboxStore(
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
    public IInboxStore For(TenantKey key) => new TenantStore(this, key, hostContext: null);

    /// <summary>
    /// Gives the tenant store of one key with the inbox in the transaction of the host (D331, D332). Its
    /// <see cref="IInboxStore.AddAsync"/> adds the row to the context of the host, and does not save, so the
    /// <c>SaveChangesAsync</c> of the host writes the row with the rows of the host. With no save, nothing is stored. An id
    /// that is stored fails that save with the <see cref="DbUpdateException"/> of EF Core. A second add of an id that the
    /// context tracks adds nothing more. Each other member uses the contexts of the factory, as the store of
    /// <see cref="For(TenantKey)"/> does. The model of the context needs <c>MapMsgHatchInbox</c>.
    /// </summary>
    /// <param name="key">The tenant key.</param>
    /// <param name="context">The context of the host.</param>
    /// <returns>The tenant store.</returns>
    public IInboxStore For(TenantKey key, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TenantStore(this, key, context);
    }

    /// <inheritdoc />
    public async ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimDueAsync(
        int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : await _contexts.RunAsync(
                context => MessageQueries.ClaimAsync(
                    context, context.Inbox, context.Inbox, channel: null, new ClaimArguments(maxCount, nowUtc, nowUtc + lease),
                    row => _json.DeserializeReceived(MessageDocuments.Received(row)), ct),
                ct);

    /// <inheritdoc />
    public ValueTask<Error?> CompleteAsync(ReceivedMessage message, DateTime triedAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Update(message, set => set
            .SetProperty(static row => row.Status, nameof(DeliveryStatus.Done))
            .SetProperty(static row => row.TryCount, static row => row.TryCount + 1)
            .SetProperty(static row => row.LastTryAtUtc, triedAtUtc)
            .SetProperty(static row => row.LeaseUntilUtc, (DateTime?)null), ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> FailAsync(
        ReceivedMessage message, string error, DateTime triedAtUtc, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var status = (nextTryAtUtc is null ? DeliveryStatus.Failed : DeliveryStatus.Pending).ToString();
        return Update(message, set => set
            .SetProperty(static row => row.Status, status)
            .SetProperty(static row => row.TryCount, static row => row.TryCount + 1)
            .SetProperty(static row => row.LastTryAtUtc, triedAtUtc)
            .SetProperty(static row => row.NextTryAtUtc, row => nextTryAtUtc ?? row.NextTryAtUtc)
            .SetProperty(static row => row.LeaseUntilUtc, (DateTime?)null)
            .SetProperty(static row => row.LastError, error), ct);
    }

    /// <inheritdoc />
    public async ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : await _contexts.RunAsync<int>(async context => await MessageQueries.PurgeAsync(context.Inbox, finishedBeforeUtc, maxCount, ct), ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        _contexts.RunAsync<int>(async context => await MessageQueries.DeleteTestDataAsync(context.Inbox, ct), ct);

    private ValueTask<Error?> Update(
        ReceivedMessage message, Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<InboxRow>> setters, CancellationToken ct)
    {
        var tenant = MessageQueries.Tenant(message.Tenant);
        var id = MessageQueries.Id(message.Delivery.Id);
        return _contexts.RunAsync(
            async context => await context.Inbox.Where(row => row.TenantId == tenant && row.Id == id).ExecuteUpdateAsync(setters, ct) == 0
                ? new Error(Errors.NotFound, "The received message is not stored.")
                : null,
            ct);
    }

    private async ValueTask<Error?> AddAsync(TenantKey key, ReceivedMessage message, DbContext? hostContext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Tenant != key.Tenant)
        {
            return new Error(Errors.ValidationFailed, "The message has another tenant than the key of the store.");
        }

        var document = _json.Serialize(message);
        if (document.IsError)
        {
            return document.Error;
        }

        if (hostContext is not null)
        {
            Stage(hostContext, MessageRows.Received(message, document.Value));
            return null;
        }

        return await _contexts.RunAsync(
            async context =>
            {
                var row = MessageRows.Received(message, document.Value);
                await RowWrites.TryAddAsync(
                    context, row, () => context.Inbox.AnyAsync(stored => stored.TenantId == row.TenantId && stored.Id == row.Id, ct), ct);
                return (Error?)null;
            },
            ct);
    }

    private async ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> QueryAsync(TenantKey key, InboxQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _contexts.RunAsync<MessagePage<ReceivedMessageSummary>>(
            async context =>
            {
                var filtered = MessageQueries.Filter(
                    context.Inbox, key, query.Ids, query.Status, query.CreatedFromUtc, query.CreatedToUtc, query.DueBeforeUtc, query.IncludeTest, query.After);
                if (query.Type is { } type)
                {
                    filtered = filtered.Where(row => row.ContentType == type.Name);
                }

                return await MessageQueries.PageAsync(
                    filtered.Select(static row => new InboxRow
                    {
                        Sequence = row.Sequence, TenantId = row.TenantId, Id = row.Id, Status = row.Status, TryCount = row.TryCount,
                        MaxTryCount = row.MaxTryCount, CreatedAtUtc = row.CreatedAtUtc, LastTryAtUtc = row.LastTryAtUtc,
                        NextTryAtUtc = row.NextTryAtUtc, LeaseUntilUtc = row.LeaseUntilUtc, ExpiresAtUtc = row.ExpiresAtUtc,
                        LastError = row.LastError, ContentType = row.ContentType, ContentVersion = row.ContentVersion, Flags = row.Flags,
                    }),
                    query.Limit, SummaryOf, static summary => summary.Delivery, ct);
            },
            ct);
    }

    private ValueTask<ErrorOr<ReceivedMessage?>> GetAsync(TenantKey key, Guid id, CancellationToken ct)
    {
        var tenant = MessageQueries.Tenant(key.Tenant);
        var text = MessageQueries.Id(id);
        return _contexts.RunAsync<ReceivedMessage?>(
            async context =>
                await context.Inbox.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenant && row.Id == text, ct) is { } row
                    ? _json.DeserializeReceived(MessageDocuments.Received(row)).ThenIfSuccess(static message => ErrorOr<ReceivedMessage?>.ForValue(message))
                    : ErrorOr<ReceivedMessage?>.ForValue(null),
            ct);
    }

    private ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TenantKey key, TrafficQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = MessageQueries.Tenant(key.Tenant);
        var status = query.Status?.ToString();
        return _contexts.RunAsync<IReadOnlyList<MessageCount>>(
            async context =>
            {
                var rows = context.Inbox.Where(row => row.TenantId == tenant);
                rows = query.CreatedFromUtc is { } from ? rows.Where(row => row.CreatedAtUtc >= from) : rows;
                rows = query.CreatedToUtc is { } to ? rows.Where(row => row.CreatedAtUtc < to) : rows;
                rows = status is null ? rows : rows.Where(row => row.Status == status);
                rows = query.IncludeTest ? rows : rows.Where(static row => row.Flags != nameof(MessageFlags.Test));
                var groups = await rows
                    .GroupBy(static row => new { row.ContentType, row.Status, Test = row.Flags == nameof(MessageFlags.Test) })
                    .Select(static group => new
                    {
                        group.Key.ContentType,
                        group.Key.Status,
                        group.Key.Test,
                        Count = group.Count(),
                        OldestDueAtUtc = group.Min(static row => row.Status == MessageQueries.Pending ? (DateTime?)row.NextTryAtUtc : null),
                    })
                    .ToListAsync(ct);
                IReadOnlyList<MessageCount> counts = [.. groups.Select(static group => new MessageCount(
                    new MessageType(group.ContentType), Channel: null, Enum.Parse<DeliveryStatus>(group.Status), group.Test, group.Count,
                    group.OldestDueAtUtc))];
                return ErrorOr<IReadOnlyList<MessageCount>>.ForValue(counts);
            },
            ct);
    }

    private ValueTask<ErrorOr<int>> CancelAsync(TenantKey key, IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
        _contexts.RunAsync<int>(
            async context => await MessageQueries.Cancellable(context.Inbox, key, ids, nowUtc).ExecuteUpdateAsync(
                static set => set
                    .SetProperty(static row => row.Status, nameof(DeliveryStatus.Cancelled))
                    .SetProperty(static row => row.LeaseUntilUtc, (DateTime?)null),
                ct),
            ct);

    private static void Stage(DbContext hostContext, InboxRow row)
    {
        if (!hostContext.ChangeTracker.Entries<InboxRow>().Any(entry => entry.Entity.TenantId == row.TenantId && entry.Entity.Id == row.Id))
        {
            hostContext.Add(row);
        }
    }

    private static ReceivedMessageSummary SummaryOf(InboxRow row) =>
        new(MessageQueries.DeliveryOf(row), MessageQueries.TenantOf(row.TenantId), new MessageType(row.ContentType), row.ContentVersion,
            Enum.Parse<MessageFlags>(row.Flags));

    private sealed class TenantStore(EntityFrameworkInboxStore store, TenantKey key, DbContext? hostContext) : IInboxStore
    {
        public ValueTask<Error?> AddAsync(ReceivedMessage message, CancellationToken ct) => store.AddAsync(key, message, hostContext, ct);

        public ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> QueryAsync(InboxQuery query, CancellationToken ct) =>
            store.QueryAsync(key, query, ct);

        public ValueTask<ErrorOr<ReceivedMessage?>> GetAsync(Guid id, CancellationToken ct) => store.GetAsync(key, id, ct);

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
            store.CountAsync(key, query, ct);

        public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
            store._contexts.RunAsync<int>(async context => await MessageQueries.RetryAsync(context.Inbox, key, ids, nowUtc, addedTries, ct), ct);

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            store.CancelAsync(key, ids, nowUtc, ct);
    }
}
