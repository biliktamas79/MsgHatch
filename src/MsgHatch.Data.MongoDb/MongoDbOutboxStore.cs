using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using static MsgHatch.Data.MongoDb.MessageOperations;

namespace MsgHatch.Data.MongoDb;

/// <summary>
/// The outbox on MongoDB (section 4.5, D121): one document of the collection <c>MsgHatchOutbox</c> for each outgoing
/// message, converted from the JSON document of <see cref="MsgHatchJson"/> (D184), with each time as <c>{ d, t }</c>
/// (D185), and its publish tries in the embedded array <c>tries</c> (D27). The collection <c>MsgHatchChannelDown</c>
/// has one document for each down channel (D106). It is the dispatch store of all tenants, and <see cref="For"/> gives
/// the tenant store of one key.
/// </summary>
public sealed class MongoDbOutboxStore : IOutboxDispatchStore
{
    /// <summary>The name of the collection of the outgoing messages.</summary>
    public const string CollectionName = "MsgHatchOutbox";

    /// <summary>The name of the collection of the down states.</summary>
    public const string ChannelDownCollectionName = "MsgHatchChannelDown";

    private const string _channel = "channel";
    private const string _tries = "tries";
    private const string _reason = "reason";
    private const string _nextCheckAtUtc = "nextCheckAtUtc";

    private static readonly FilterDefinitionBuilder<BsonDocument> _filter = Builders<BsonDocument>.Filter;

    private static readonly string[] _content = ["trace", "envelope", _tries];

    private readonly MongoDbStorage _storage;
    private readonly IMongoCollection<BsonDocument> _downs;
    private readonly MsgHatchJson _json;

    /// <summary>Makes the store on a database.</summary>
    /// <param name="database">The database of the stores.</param>
    /// <param name="json">The serializer of the messages.</param>
    public MongoDbOutboxStore(IMongoDatabase database, MsgHatchJson json)
    {
        ArgumentNullException.ThrowIfNull(json);
        _storage = new MongoDbStorage(
            database,
            CollectionName,
            [
                Index("UX_MsgHatchOutbox_Id", new BsonDocument(MessageId, 1), unique: true),
                DueIndex("IX_MsgHatchOutbox_Due", _channel),
                Index("IX_MsgHatchOutbox_Flags", new BsonDocument(Flags, 1)),
                Index("IX_MsgHatchOutbox_Created", new BsonDocument { { Tenant, 1 }, { CreatedAtTicks, 1 }, { MessageId, 1 } }),
                Index("IX_MsgHatchOutbox_Target", new BsonDocument { { Tenant, 1 }, { "target.kind", 1 }, { "target.id", 1 }, { CreatedAtTicks, 1 } }),
                Index("IX_MsgHatchOutbox_Source", new BsonDocument("sourceMessageId", 1)),
            ]);
        _downs = database.GetCollection<BsonDocument>(ChannelDownCollectionName);
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
            : await _storage.RunAsync(
                async () => await _downs.Find(_filter.Eq(Id, channel.Name)).FirstOrDefaultAsync(ct) is { } down
                    ? new ClaimResult<OutgoingMessage>([], NextDueAtUtc: null, DownOf(down))
                    : await ClaimAsync(_storage.Documents, _filter.Eq(_channel, channel.Name), maxCount, nowUtc, lease, Read, ct),
                ct);

    /// <inheritdoc />
    public ValueTask<Error?> RecordTryAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publishTry);
        var status = publishTry.StatusAfter(nextTryAtUtc);
        var id = IdText(message.Delivery.Id);
        var update = Builders<BsonDocument>.Update
            .Push(_tries, TryDocument(publishTry))
            .Set(Status, status.ToString())
            .Set(TryCount, publishTry.TryNumber)
            .Set(LastTryAtUtc, BsonDocuments.Time(publishTry.FinishedAtUtc))
            .Set(LeaseUntilUtc, BsonNull.Value)
            .Set(LastError, BsonDocuments.Text(publishTry.ErrorText()));
        if (status == DeliveryStatus.Pending && nextTryAtUtc is { } next)
        {
            update = update.Set(NextTryAtUtc, BsonDocuments.Time(next));
        }

        return _storage.RunAsync(
            async () =>
            {
                var result = await _storage.Documents.UpdateOneAsync(
                    _filter.And(_filter.Eq(MessageId, id), _filter.Eq(TryCount, publishTry.TryNumber - 1)), update, cancellationToken: ct);
                return result.MatchedCount == 0 && await _storage.Documents.CountDocumentsAsync(_filter.Eq(MessageId, id), cancellationToken: ct) == 0
                    ? new Error(Errors.NotFound, "The outgoing message is not stored.")
                    : null;
            },
            ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) => Count(_filter.Empty, query, ct);

    /// <inheritdoc />
    public async ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : await _storage.RunAsync<int>(async () => await MessageOperations.PurgeAsync(_storage.Documents, finishedBeforeUtc, maxCount, ct), ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        _storage.RunAsync<int>(async () => await MessageOperations.DeleteTestDataAsync(_storage.Documents, ct), ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct) =>
        _storage.RunAsync<IReadOnlyList<ChannelDown>>(
            async () =>
            {
                var documents = await _downs.Find(_filter.Empty).Sort(Builders<BsonDocument>.Sort.Ascending(Id)).ToListAsync(ct);
                IReadOnlyList<ChannelDown> downs = [.. documents.Select(DownOf)];
                return ErrorOr<IReadOnlyList<ChannelDown>>.ForValue(downs);
            },
            ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(down);
        var document = new BsonDocument
        {
            { Id, down.Channel.Name },
            { _reason, down.Reason.ToString() },
            { "sinceUtc", BsonDocuments.Time(down.SinceUtc) },
            { _nextCheckAtUtc, BsonDocuments.Time(down.NextCheckAtUtc) },
            { "errorCode", down.ErrorCode is { } code ? new BsonInt32(code) : BsonNull.Value },
            { "errorName", BsonDocuments.Text(down.ErrorName) },
            { "note", BsonDocuments.Text(down.Note) },
        };
        return _storage.RunAsync<bool>(
            async () =>
            {
                if (down.Reason == ChannelDownReason.Operator)
                {
                    await _downs.ReplaceOneAsync(_filter.Eq(Id, down.Channel.Name), document, new ReplaceOptions { IsUpsert = true }, ct);
                    return true;
                }

                try
                {
                    await _downs.InsertOneAsync(document, cancellationToken: ct);
                    return true;
                }
                catch (MongoException exception) when (MongoDbStorage.IsDuplicateKey(exception))
                {
                    return false;
                }
            },
            ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelStatesAsync(DateTime fromUtc, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelState>>.ForValue([]));

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, DateTime upAtUtc, CancellationToken ct)
    {
        var filter = _filter.Eq(Id, channel.Name);
        filter = reason is { } value ? filter & _filter.Eq(_reason, value.ToString()) : filter;
        return _storage.RunAsync<bool>(async () => (await _downs.DeleteOneAsync(filter, ct)).DeletedCount > 0, ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct) =>
        _storage.RunAsync<bool>(
            async () => (await _downs.UpdateOneAsync(
                _filter.And(
                    _filter.Eq(Id, channel.Name), _filter.Eq(_reason, nameof(ChannelDownReason.Errors)),
                    _filter.Lte($"{_nextCheckAtUtc}.{BsonDocuments.Ticks}", nowUtc.Ticks)),
                Builders<BsonDocument>.Update.Set(_nextCheckAtUtc, BsonDocuments.Time(nextCheckAtUtc)),
                cancellationToken: ct)).MatchedCount > 0,
            ct);

    /// <summary>A publish try as one document of the array <c>tries</c>, with the names of section 4.5.</summary>
    private static BsonDocument TryDocument(PublishTry publishTry) =>
        new()
        {
            { "tryNumber", publishTry.TryNumber },
            { "startedAtUtc", BsonDocuments.Time(publishTry.StartedAtUtc) },
            { "finishedAtUtc", BsonDocuments.Time(publishTry.FinishedAtUtc) },
            { "outcome", publishTry.Outcome.ToString() },
            { "providerMessageId", BsonDocuments.Text(publishTry.ProviderMessageId) },
            { "errorCode", publishTry.ErrorCode is { } code ? new BsonInt32(code) : BsonNull.Value },
            { "errorName", BsonDocuments.Text(publishTry.ErrorName) },
            { "errorMessage", BsonDocuments.Text(publishTry.ErrorMessage) },
        };

    /// <exception cref="ArgumentException">The document has an invalid channel name or reason.</exception>
    /// <exception cref="InvalidCastException">A field has another BSON type.</exception>
    private static ChannelDown DownOf(BsonDocument document) =>
        new(
            new ChannelId(document[Id].AsString), Enum.Parse<ChannelDownReason>(document[_reason].AsString),
            BsonDocuments.TimeOf(document["sinceUtc"]), BsonDocuments.NullableTimeOf(document[_nextCheckAtUtc]),
            document["errorCode"].IsBsonNull ? null : document["errorCode"].AsInt32, BsonDocuments.NullableTextOf(document["errorName"]),
            BsonDocuments.NullableTextOf(document["note"]));

    private ErrorOr<OutgoingMessage> Read(BsonDocument document) => _json.DeserializeOutgoing(BsonDocuments.ToJson(document, Id, _tries));

    private async ValueTask<Error?> AddAsync(TenantKey key, IReadOnlyList<OutgoingMessage> messages, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Any(message => message.Tenant != key.Tenant))
        {
            return new Error(Errors.ValidationFailed, "A message has another tenant than the key of the store.");
        }

        var documents = new List<BsonDocument>(messages.Count);
        foreach (var message in messages)
        {
            var json = _json.Serialize(message);
            if (json.IsError)
            {
                return json.Error;
            }

            var document = MongoDbStorage.Read(() => ErrorOr<BsonDocument>.ForValue(
                WithTimesAndId(BsonDocuments.FromJson(json.Value), message.Delivery).Add(_tries, new BsonArray())));
            if (document.IsError)
            {
                return document.Error;
            }

            documents.Add(document.Value);
        }

        return documents.Count == 0
            ? null
            : await _storage.RunAsync(
                async () =>
                {
                    await MongoDbStorage.InsertNewAsync(_storage.Documents, documents, ct);
                    return (Error?)null;
                },
                ct);
    }

    private ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(TenantKey key, OutboxQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = Filter(key, query.Ids, query.Status, query.CreatedFromUtc, query.CreatedToUtc, query.DueBeforeUtc, query.IncludeTest, query.After);
        if (query.Type is { } type)
        {
            filter &= _filter.Eq(MessageTypeField, type.Name);
        }

        if (query.Channel is { } channel)
        {
            filter &= _filter.Eq(_channel, channel.Name);
        }

        if (query.Target is { } target)
        {
            filter &= _filter.Eq("target.kind", target.Kind.ToString()) & _filter.Eq("target.id", target.Id);
        }

        if (query.SourceMessageId is { } source)
        {
            filter &= _filter.Eq("sourceMessageId", IdText(source));
        }

        return _storage.RunAsync<MessagePage<OutgoingMessageSummary>>(
            async () => await PageAsync(_storage.Documents, filter, query.Limit, _content, SummaryOf, static summary => summary.Delivery, ct),
            ct);
    }

    private ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(TenantKey key, Guid id, CancellationToken ct) =>
        _storage.RunAsync<OutgoingMessageHistory?>(
            async () =>
            {
                if (await _storage.Documents.Find(_filter.And(OfTenant(key.Tenant), _filter.Eq(MessageId, IdText(id)))).FirstOrDefaultAsync(ct)
                    is not { } document)
                {
                    return ErrorOr<OutgoingMessageHistory?>.ForValue(null);
                }

                var message = Read(document);
                var tries = MsgHatchJson.DeserializeTries(id, Encoding.UTF8.GetBytes(BsonDocuments.ToJsonText(document[_tries])));
                return message.Error ?? tries.Error ?? (ErrorOr<OutgoingMessageHistory?>)new OutgoingMessageHistory(message.Value, tries.Value);
            },
            ct);

    /// <param name="scope">The documents of one tenant, or of all tenants.</param>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancel token.</param>
    private ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> Count(FilterDefinition<BsonDocument> scope, TrafficQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = Traffic(scope, query);
        return _storage.RunAsync<IReadOnlyList<MessageCount>>(
            async () =>
            {
                var groups = await _storage.Documents.Aggregate()
                    .Match(filter)
                    .Group(CountGroup(new BsonDocument
                    {
                        { "type", "$type" }, { _channel, "$channel" }, { "status", "$delivery.status" }, { "test", IsTestExpression },
                    }))
                    .ToListAsync(ct);
                IReadOnlyList<MessageCount> counts = [.. groups.Select(static group => new MessageCount(
                    new MessageType(group["_id"]["type"].AsString), new ChannelId(group["_id"][_channel].AsString),
                    Enum.Parse<DeliveryStatus>(group["_id"]["status"].AsString), group["_id"]["test"].AsBoolean, group["count"].AsInt32,
                    OldestOf(group)))];
                return ErrorOr<IReadOnlyList<MessageCount>>.ForValue(counts);
            },
            ct);
    }

    /// <summary>
    /// Cancels the pending documents of the ids with no active lease, and appends the cancelled try with the next try
    /// number of each document (D110). One pipeline update reads the stored try count of each document.
    /// </summary>
    private ValueTask<ErrorOr<int>> CancelAsync(TenantKey key, IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct)
    {
        var filter = _filter.And(OfIds(key, ids), _filter.Eq(Status, Pending), NoActiveLease(nowUtc));
        var nextTryNumber = new BsonDocument("$add", new BsonArray { "$delivery.tryCount", 1 });
        var cancelledTry = new BsonDocument(TryDocument(PublishTry.Cancel(Guid.Empty, tryNumber: 0, nowUtc)).Select(static element =>
            new BsonElement(element.Name, Literal(element.Value))))
        {
            ["tryNumber"] = nextTryNumber,
        };
        var cancel = new BsonDocument("$set", new BsonDocument
        {
            { Status, Literal(nameof(DeliveryStatus.Cancelled)) },
            { LeaseUntilUtc, BsonNull.Value },
            { TryCount, nextTryNumber },
            { LastTryAtUtc, Literal(BsonDocuments.Time(nowUtc)) },
            { _tries, new BsonDocument("$concatArrays", new BsonArray { "$tries", new BsonArray { cancelledTry } }) },
        });
        return _storage.RunAsync<int>(
            async () => (int)(await _storage.Documents.UpdateManyAsync(filter, Pipeline(cancel), cancellationToken: ct)).MatchedCount,
            ct);
    }

    private static OutgoingMessageSummary SummaryOf(BsonDocument document) =>
        new(
            DeliveryOf(document), Guid.Parse(document["sourceMessageId"].AsString), TenantOf(document[Tenant]), new MessageType(document[MessageTypeField].AsString),
            new ChannelId(document[_channel].AsString),
            new DeliveryTarget(Enum.Parse<DeliveryTargetKind>(document["target"]["kind"].AsString), document["target"]["id"].AsString),
            document["contentType"].AsString, document["contentVersion"].AsInt32, Enum.Parse<MessageFlags>(document[Flags].AsString));

    private sealed class TenantStore(MongoDbOutboxStore store, TenantKey key) : IOutboxStore
    {
        public ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct) => store.AddAsync(key, messages, ct);

        public ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct) =>
            store.QueryAsync(key, query, ct);

        public ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct) => store.GetAsync(key, id, ct);

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
            store.Count(OfTenant(key.Tenant), query, ct);

        public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
            store._storage.RunAsync<int>(async () => await MessageOperations.RetryAsync(store._storage.Documents, key, ids, nowUtc, addedTries, ct), ct);

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            store.CancelAsync(key, ids, nowUtc, ct);
    }
}
