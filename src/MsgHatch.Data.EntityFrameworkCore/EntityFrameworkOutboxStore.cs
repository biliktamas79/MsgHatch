using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Data.EntityFrameworkCore;

/// <summary>
/// The outbox on EF Core (section 4.5, D120): one row of the table <c>MsgHatchOutbox</c> for each outgoing message, with
/// the columns of its delivery state, its channel envelope and its route trace as JSON (D143), and its publish tries as
/// the JSON array <c>Tries</c> (D27). The table <c>MsgHatchChannelDown</c> has one row for each down channel (D106).
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
    public EntityFrameworkOutboxStore(IDbContextFactory<MsgHatchDbContext> contexts, MsgHatchJson json)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(json);
        _contexts = new StoreContexts<MsgHatchDbContext>(contexts);
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
                    if (await ReadDownAsync(context, channel, ct) is { } down)
                    {
                        return new ClaimResult<OutgoingMessage>([], NextDueAtUtc: null, down);
                    }

                    var arguments = Arguments(context)
                        .Number("@maxCount", maxCount)
                        .Name("@channel", channel.Name)
                        .Time("@nowUtc", nowUtc)
                        .Time("@leaseUntilUtc", nowUtc + lease);
                    return await MessageQueries.ClaimAsync(
                        context.Outbox.FromSqlRaw(Statements(context).ClaimOutgoing, arguments.Values),
                        context.Outbox.Where(row => row.Channel == channel.Name), maxCount, nowUtc,
                        row => _json.DeserializeOutgoing(MessageDocuments.Outgoing(row)), ct);
                },
                ct);

    /// <inheritdoc />
    public ValueTask<Error?> RecordTryAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publishTry);
        var status = publishTry.StatusAfter(nextTryAtUtc);
        var id = MessageQueries.Id(message.Delivery.Id);
        return _contexts.RunAsync(
            async context =>
            {
                var arguments = Arguments(context)
                    .Text("@id", id)
                    .Text("@try", TryJson(publishTry))
                    .Number("@tryNumber", publishTry.TryNumber)
                    .Time("@finishedAtUtc", publishTry.FinishedAtUtc)
                    .Name("@status", status.ToString())
                    .Time("@nextTryAtUtc", status == DeliveryStatus.Pending ? nextTryAtUtc : null)
                    .Text("@lastError", publishTry.ErrorText());
                return await context.Database.ExecuteSqlRawAsync(Statements(context).RecordTry, arguments.Values, ct) == 0
                    && !await context.Outbox.AnyAsync(row => row.Id == id, ct)
                    ? new Error(Errors.NotFound, "The outgoing message is not stored.")
                    : null;
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
                var rows = await context.ChannelDowns.AsNoTracking().ToListAsync(ct);
                IReadOnlyList<ChannelDown> downs = [.. rows.Select(DownOf)];
                return ErrorOr<IReadOnlyList<ChannelDown>>.ForValue(downs);
            },
            ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(down);
        return _contexts.RunAsync<bool>(
            async context =>
            {
                var arguments = Arguments(context)
                    .Name("@channel", down.Channel.Name)
                    .Name("@reason", down.Reason.ToString())
                    .Time("@sinceUtc", down.SinceUtc)
                    .Time("@nextCheckAtUtc", down.NextCheckAtUtc)
                    .Number("@errorCode", down.ErrorCode)
                    .Name("@errorName", down.ErrorName)
                    .Text("@note", down.Note);
                var statements = Statements(context);
                return await context.Database.ExecuteSqlRawAsync(
                    down.Reason == ChannelDownReason.Errors ? statements.SetChannelDownOnce : statements.SetChannelDown, arguments.Values, ct) > 0;
            },
            ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, CancellationToken ct)
    {
        var text = reason?.ToString();
        return _contexts.RunAsync<bool>(
            async context => await context.ChannelDowns
                .Where(row => row.Channel == channel.Name && (text == null || row.Reason == text))
                .ExecuteDeleteAsync(ct) > 0,
            ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct) =>
        _contexts.RunAsync<bool>(
            async context => await context.ChannelDowns
                .Where(row => row.Channel == channel.Name && row.Reason == nameof(ChannelDownReason.Errors) && row.NextCheckAtUtc <= nowUtc)
                .ExecuteUpdateAsync(set => set.SetProperty(static row => row.NextCheckAtUtc, nextCheckAtUtc), ct) > 0,
            ct);

    private static SqlArguments Arguments(MsgHatchDbContext context) => new(context.Database.GetDbConnection());

    private static SqlServerStatements Statements(MsgHatchDbContext context) => new(context.Model.GetDefaultSchema()!);

    /// <summary>A publish try as one JSON object of the array <c>Tries</c> (section 4.5).</summary>
    private static string TryJson(PublishTry publishTry)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("tryNumber", publishTry.TryNumber);
            writer.WriteString("startedAtUtc", MessageDocuments.Time(publishTry.StartedAtUtc));
            writer.WriteString("finishedAtUtc", MessageDocuments.Time(publishTry.FinishedAtUtc));
            writer.WriteString("outcome", publishTry.Outcome.ToString());
            writer.WriteString("providerMessageId", publishTry.ProviderMessageId);
            if (publishTry.ErrorCode is { } code)
            {
                writer.WriteNumber("errorCode", code);
            }
            else
            {
                writer.WriteNull("errorCode");
            }

            writer.WriteString("errorName", publishTry.ErrorName);
            writer.WriteString("errorMessage", publishTry.ErrorMessage);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static async Task<ChannelDown?> ReadDownAsync(MsgHatchDbContext context, ChannelId channel, CancellationToken ct) =>
        await context.ChannelDowns.AsNoTracking().FirstOrDefaultAsync(row => row.Channel == channel.Name, ct) is { } row ? DownOf(row) : null;

    /// <exception cref="ArgumentException">The row has an invalid channel name or reason.</exception>
    private static ChannelDown DownOf(ChannelDownRow row) =>
        new(new ChannelId(row.Channel), Enum.Parse<ChannelDownReason>(row.Reason), row.SinceUtc, row.NextCheckAtUtc, row.ErrorCode, row.ErrorName,
            row.Note);

    private async ValueTask<Error?> AddAsync(TenantKey key, IReadOnlyList<OutgoingMessage> messages, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Any(message => message.Tenant != key.Tenant))
        {
            return new Error(Errors.ValidationFailed, "A message has another tenant than the key of the store.");
        }

        var documents = new List<(OutgoingMessage Message, string Document)>(messages.Count);
        foreach (var message in messages)
        {
            var document = _json.Serialize(message);
            if (document.IsError)
            {
                return document.Error;
            }

            documents.Add((message, Encoding.UTF8.GetString(document.Value)));
        }

        return await _contexts.RunAsync(
            async context =>
            {
                var insert = Statements(context).InsertOutgoing;
                await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(ct);
                    foreach (var (message, document) in documents)
                    {
                        await context.Database.ExecuteSqlRawAsync(insert, InsertArguments(context, message, document).Values, ct);
                    }

                    await transaction.CommitAsync(ct);
                });
                return (Error?)null;
            },
            ct);
    }

    private static SqlArguments InsertArguments(MsgHatchDbContext context, OutgoingMessage message, string document) =>
        Arguments(context)
            .Text("@tenantId", MessageQueries.Tenant(message.Tenant))
            .Delivery(message.Delivery)
            .Number("@schemaVersion", message.SchemaVersion)
            .Text("@sourceMessageId", MessageQueries.Id(message.SourceMessageId))
            .Name("@messageType", message.Type.Name)
            .Name("@channel", message.Channel.Name)
            .Name("@targetKind", message.Target.Kind.ToString())
            .Text("@targetId", message.Target.Id)
            .Name("@contentType", message.ContentType)
            .Number("@contentVersion", message.ContentVersion)
            .Name("@flags", message.Flags.ToString())
            .Text("@traceParent", message.TraceParent)
            .Text("@document", document);

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
                var arguments = Arguments(context)
                    .Text("@tenantId", MessageQueries.Tenant(key.Tenant))
                    .Text("@ids", $"[{string.Join(',', ids.Select(static id => $"\"{MessageQueries.Id(id)}\""))}]")
                    .Time("@nowUtc", nowUtc)
                    .Text("@try", TryJson(PublishTry.Cancel(Guid.Empty, tryNumber: 0, nowUtc)));
                return await context.Database.ExecuteSqlRawAsync(Statements(context).CancelOutgoing, arguments.Values, ct);
            },
            ct);
    }

    private static OutgoingMessageSummary SummaryOf(OutboxRow row) =>
        new(
            MessageQueries.DeliveryOf(row), Guid.Parse(row.SourceMessageId), MessageQueries.TenantOf(row.TenantId), new MessageType(row.MessageType),
            new ChannelId(row.Channel), new DeliveryTarget(Enum.Parse<DeliveryTargetKind>(row.TargetKind), row.TargetId), row.ContentType,
            row.ContentVersion, Enum.Parse<MessageFlags>(row.Flags));

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
