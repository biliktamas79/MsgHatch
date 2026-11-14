using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using static MsgHatch.Data.MongoDb.MessageOperations;

namespace MsgHatch.Data.MongoDb;

/// <summary>
/// The inbox on MongoDB (section 4.5, D121): one document of the collection <c>MsgHatchInbox</c> for each received
/// message, converted from the JSON document of <see cref="MsgHatchJson"/> (D184), with each time as <c>{ d, t }</c>
/// (D185). The key of a document is its tenant and its id, with a unique index. It is the dispatch store of all
/// tenants, and <see cref="For"/> gives the tenant store of one key.
/// </summary>
public sealed class MongoDbInboxStore : IInboxDispatchStore
{
    /// <summary>The name of the collection.</summary>
    public const string CollectionName = "MsgHatchInbox";

    private static readonly FilterDefinitionBuilder<BsonDocument> _filter = Builders<BsonDocument>.Filter;

    private static readonly string[] _content = ["event"];

    private readonly MongoDbStorage _storage;
    private readonly MsgHatchJson _json;

    /// <summary>Makes the store on a database.</summary>
    /// <param name="database">The database of the stores.</param>
    /// <param name="json">The serializer of the messages.</param>
    public MongoDbInboxStore(IMongoDatabase database, MsgHatchJson json)
    {
        ArgumentNullException.ThrowIfNull(json);
        _storage = new MongoDbStorage(
            database,
            CollectionName,
            [
                Index("UX_MsgHatchInbox_Key", new BsonDocument { { Tenant, 1 }, { MessageId, 1 } }, unique: true),
                DueIndex("IX_MsgHatchInbox_Due"),
                Index("IX_MsgHatchInbox_Flags", new BsonDocument(Flags, 1)),
                Index("IX_MsgHatchInbox_Created", new BsonDocument { { Tenant, 1 }, { CreatedAtTicks, 1 }, { MessageId, 1 } }),
            ]);
        _json = json;
    }

    /// <summary>Gives the tenant store of one key. It reads and writes only the messages of its tenant.</summary>
    /// <param name="key">The tenant key.</param>
    /// <returns>The tenant store.</returns>
    public IInboxStore For(TenantKey key) => new TenantStore(this, key);

    /// <inheritdoc />
    public async ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimDueAsync(
        int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : await _storage.RunAsync(
                () => ClaimAsync(_storage.Documents, _filter.Empty, maxCount, nowUtc, lease, Read, ct),
                ct);

    /// <inheritdoc />
    public ValueTask<Error?> CompleteAsync(ReceivedMessage message, DateTime triedAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        return UpdateAsync(
            message,
            Builders<BsonDocument>.Update
                .Set(Status, nameof(DeliveryStatus.Done))
                .Inc(TryCount, 1)
                .Set(LastTryAtUtc, BsonDocuments.Time(triedAtUtc))
                .Set(LeaseUntilUtc, BsonNull.Value),
            ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> FailAsync(
        ReceivedMessage message, string error, DateTime triedAtUtc, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var update = Builders<BsonDocument>.Update
            .Set(Status, (nextTryAtUtc is null ? DeliveryStatus.Failed : DeliveryStatus.Pending).ToString())
            .Inc(TryCount, 1)
            .Set(LastTryAtUtc, BsonDocuments.Time(triedAtUtc))
            .Set(LeaseUntilUtc, BsonNull.Value)
            .Set(LastError, error);
        return UpdateAsync(message, nextTryAtUtc is { } next ? update.Set(NextTryAtUtc, BsonDocuments.Time(next)) : update, ct);
    }

    /// <inheritdoc />
    public async ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : await _storage.RunAsync<int>(async () => await MessageOperations.PurgeAsync(_storage.Documents, finishedBeforeUtc, maxCount, ct), ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        _storage.RunAsync<int>(async () => await MessageOperations.DeleteTestDataAsync(_storage.Documents, ct), ct);

    private ErrorOr<ReceivedMessage> Read(BsonDocument document) => _json.DeserializeReceived(BsonDocuments.ToJson(document, Id));

    private ValueTask<Error?> UpdateAsync(ReceivedMessage message, UpdateDefinition<BsonDocument> update, CancellationToken ct) =>
        _storage.RunAsync(
            async () =>
            {
                var result = await _storage.Documents.UpdateOneAsync(
                    _filter.And(OfTenant(message.Tenant), _filter.Eq(MessageId, IdText(message.Delivery.Id))), update, cancellationToken: ct);
                return result.MatchedCount == 0 ? new Error(Errors.NotFound, "The received message is not stored.") : null;
            },
            ct);

    private async ValueTask<Error?> AddAsync(TenantKey key, ReceivedMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Tenant != key.Tenant)
        {
            return new Error(Errors.ValidationFailed, "The message has another tenant than the key of the store.");
        }

        var json = _json.Serialize(message);
        if (json.IsError)
        {
            return json.Error;
        }

        var document = MongoDbStorage.Read(() => ErrorOr<BsonDocument>.ForValue(WithTimesAndId(BsonDocuments.FromJson(json.Value), message.Delivery)));
        if (document.IsError)
        {
            return document.Error;
        }

        return await _storage.RunAsync(
            async () =>
            {
                await MongoDbStorage.InsertNewAsync(_storage.Documents, [document.Value], ct);
                return (Error?)null;
            },
            ct);
    }

    private ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> QueryAsync(TenantKey key, InboxQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = Filter(key, query.Ids, query.Status, query.CreatedFromUtc, query.CreatedToUtc, query.DueBeforeUtc, query.IncludeTest, query.After);
        if (query.Type is { } type)
        {
            filter &= _filter.Eq(MessageTypeField, type.Name);
        }

        return _storage.RunAsync<MessagePage<ReceivedMessageSummary>>(
            async () => await PageAsync(_storage.Documents, filter, query.Limit, _content, SummaryOf, static summary => summary.Delivery, ct),
            ct);
    }

    private ValueTask<ErrorOr<ReceivedMessage?>> GetAsync(TenantKey key, Guid id, CancellationToken ct) =>
        _storage.RunAsync<ReceivedMessage?>(
            async () =>
                await _storage.Documents.Find(_filter.And(OfTenant(key.Tenant), _filter.Eq(MessageId, IdText(id)))).FirstOrDefaultAsync(ct) is { } document
                    ? Read(document).ThenIfSuccess(static message => ErrorOr<ReceivedMessage?>.ForValue(message))
                    : ErrorOr<ReceivedMessage?>.ForValue(null),
            ct);

    private ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TenantKey key, TrafficQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = Traffic(OfTenant(key.Tenant), query);
        return _storage.RunAsync<IReadOnlyList<MessageCount>>(
            async () =>
            {
                var groups = await _storage.Documents.Aggregate()
                    .Match(filter)
                    .Group(CountGroup(new BsonDocument { { "type", "$type" }, { "status", "$delivery.status" }, { "test", IsTestExpression } }))
                    .ToListAsync(ct);
                IReadOnlyList<MessageCount> counts = [.. groups.Select(static group => new MessageCount(
                    new MessageType(group["_id"]["type"].AsString), Channel: null, Enum.Parse<DeliveryStatus>(group["_id"]["status"].AsString),
                    group["_id"]["test"].AsBoolean, group["count"].AsInt32, OldestOf(group)))];
                return ErrorOr<IReadOnlyList<MessageCount>>.ForValue(counts);
            },
            ct);
    }

    private ValueTask<ErrorOr<int>> CancelAsync(TenantKey key, IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
        _storage.RunAsync<int>(
            async () => (int)(await _storage.Documents.UpdateManyAsync(
                _filter.And(OfIds(key, ids), _filter.Eq(Status, Pending), NoActiveLease(nowUtc)),
                Builders<BsonDocument>.Update.Set(Status, nameof(DeliveryStatus.Cancelled)).Set(LeaseUntilUtc, BsonNull.Value),
                cancellationToken: ct)).MatchedCount,
            ct);

    private static ReceivedMessageSummary SummaryOf(BsonDocument document) =>
        new(DeliveryOf(document), TenantOf(document[Tenant]), new MessageType(document[MessageTypeField].AsString), document["contentVersion"].AsInt32,
            Enum.Parse<MessageFlags>(document[Flags].AsString));

    private sealed class TenantStore(MongoDbInboxStore store, TenantKey key) : IInboxStore
    {
        public ValueTask<Error?> AddAsync(ReceivedMessage message, CancellationToken ct) => store.AddAsync(key, message, ct);

        public ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> QueryAsync(InboxQuery query, CancellationToken ct) =>
            store.QueryAsync(key, query, ct);

        public ValueTask<ErrorOr<ReceivedMessage?>> GetAsync(Guid id, CancellationToken ct) => store.GetAsync(key, id, ct);

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
            store.CountAsync(key, query, ct);

        public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
            store._storage.RunAsync<int>(async () => await MessageOperations.RetryAsync(store._storage.Documents, key, ids, nowUtc, addedTries, ct), ct);

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            store.CancelAsync(key, ids, nowUtc, ct);
    }
}
