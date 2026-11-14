using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Data.MongoDb;

/// <summary>
/// The operations that the inbox and the outbox share on their message documents: the field names of the document of
/// <c>MsgHatchJson</c>, the claim (D103, D121, D144, D187), the filters and the page of a query (D112), the retry
/// (D111), the purge (D128), and the delivery state of a document. Each filter and sort on a time uses its ticks
/// <c>t</c> (D185).
/// </summary>
internal static class MessageOperations
{
    public const string Id = "_id";
    public const string MessageId = "delivery.id";
    public const string Status = "delivery.status";
    public const string TryCount = "delivery.tryCount";
    public const string LastTryAtUtc = "delivery.lastTryAtUtc";
    public const string NextTryAtUtc = "delivery.nextTryAtUtc";
    public const string LeaseUntilUtc = "delivery.leaseUntilUtc";
    public const string LastError = "delivery.lastError";
    public const string Tenant = "tenant";
    public const string MessageTypeField = "type";
    public const string Flags = "flags";
    public const string Pending = nameof(DeliveryStatus.Pending);
    public const string Test = nameof(MessageFlags.Test);

    public const string CreatedAtTicks = "delivery.createdAtUtc.t";
    public const string NextTryAtTicks = "delivery.nextTryAtUtc.t";

    private const string _failed = nameof(DeliveryStatus.Failed);
    private const string _leaseUntilTicks = "delivery.leaseUntilUtc.t";
    private const string _expiresAtTicks = "delivery.expiresAtUtc.t";

    private static readonly FilterDefinitionBuilder<BsonDocument> _filter = Builders<BsonDocument>.Filter;

    /// <summary>The claim order: the due time, then the ObjectId of the add (D144, D187).</summary>
    public static readonly SortDefinition<BsonDocument> ClaimOrder =
        Builders<BsonDocument>.Sort.Ascending(NextTryAtTicks).Ascending(Id);

    /// <summary>The claim index of a collection with the keys before the due time, with only the pending documents (D104).</summary>
    public static CreateIndexModel<BsonDocument> DueIndex(string name, params string[] leadingKeys)
    {
        var keys = new BsonDocument(leadingKeys.Select(static key => new BsonElement(key, 1)))
        {
            { NextTryAtTicks, 1 },
            { Id, 1 },
        };
        return new CreateIndexModel<BsonDocument>(
            keys,
            new CreateIndexOptions<BsonDocument> { Name = name, PartialFilterExpression = _filter.Eq(Status, Pending) });
    }

    public static CreateIndexModel<BsonDocument> Index(string name, BsonDocument keys, bool unique = false) =>
        new(keys, new CreateIndexOptions { Name = name, Unique = unique });

    public static string IdText(Guid id) => id.ToString("D");

    public static BsonValue TenantValue(TenantId? tenant) => BsonDocuments.Text(tenant?.Value);

    /// <exception cref="InvalidCastException">The value is not a text or null.</exception>
    /// <exception cref="ArgumentException">The text is not a valid tenant id.</exception>
    public static TenantId? TenantOf(BsonValue value) => BsonDocuments.NullableTextOf(value) is { } text ? new TenantId(text) : null;

    public static FilterDefinition<BsonDocument> OfTenant(TenantId? tenant) => _filter.Eq(Tenant, TenantValue(tenant));

    /// <summary>The delivery state of a message document.</summary>
    /// <exception cref="InvalidCastException">A field has another BSON type.</exception>
    /// <exception cref="KeyNotFoundException">A field is missing.</exception>
    /// <exception cref="FormatException">The id is not a <see cref="Guid"/>.</exception>
    /// <exception cref="ArgumentException">The status is not a <see cref="DeliveryStatus"/>.</exception>
    public static DeliveryState DeliveryOf(BsonDocument document)
    {
        var delivery = document["delivery"].AsBsonDocument;
        return new DeliveryState(
            Guid.Parse(delivery["id"].AsString),
            Enum.Parse<DeliveryStatus>(delivery["status"].AsString),
            delivery["tryCount"].AsInt32,
            delivery["maxTryCount"].AsInt32,
            BsonDocuments.TimeOf(delivery["createdAtUtc"]),
            BsonDocuments.NullableTimeOf(delivery["lastTryAtUtc"]),
            BsonDocuments.TimeOf(delivery["nextTryAtUtc"]),
            BsonDocuments.NullableTimeOf(delivery["leaseUntilUtc"]),
            BsonDocuments.NullableTimeOf(delivery["expiresAtUtc"]),
            BsonDocuments.NullableTextOf(delivery["lastError"]));
    }

    /// <summary>
    /// Writes the times of a delivery state into the delivery document of a converted message as <c>{ d, t }</c>
    /// (D185), and gives the document its ObjectId from the time of the add and the counter of the driver (D187).
    /// </summary>
    public static BsonDocument WithTimesAndId(BsonDocument document, DeliveryState state)
    {
        var delivery = document["delivery"].AsBsonDocument;
        delivery["createdAtUtc"] = BsonDocuments.Time(state.CreatedAtUtc);
        delivery["lastTryAtUtc"] = BsonDocuments.Time(state.LastTryAtUtc);
        delivery["nextTryAtUtc"] = BsonDocuments.Time(state.NextTryAtUtc);
        delivery["leaseUntilUtc"] = BsonDocuments.Time(state.LeaseUntilUtc);
        delivery["expiresAtUtc"] = BsonDocuments.Time(state.ExpiresAtUtc);
        var seconds = (int)((state.CreatedAtUtc.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerSecond);
        document.InsertAt(0, new BsonElement(Id, ObjectId.GenerateNewId(seconds)));
        return document;
    }

    /// <summary>
    /// Claims up to <paramref name="maxCount"/> due documents of a scope with one <c>findOneAndUpdate</c> for each
    /// document, in the claim order (D121, D144). Each call sets the due time and the lease end to the end of the lease
    /// (D103). A document that the code cannot read stays out of the result: a transient error keeps it leased for a
    /// newer instance (section 4.13), and a permanent error fails it.
    /// </summary>
    public static async ValueTask<ErrorOr<ClaimResult<T>>> ClaimAsync<T>(
        IMongoCollection<BsonDocument> documents, FilterDefinition<BsonDocument> scope, int maxCount, DateTime nowUtc, TimeSpan lease,
        Func<BsonDocument, ErrorOr<T>> read, CancellationToken ct)
    {
        var leaseEnd = BsonDocuments.Time(nowUtc + lease);
        var due = _filter.And(scope, _filter.Eq(Status, Pending), _filter.Lte(NextTryAtTicks, nowUtc.Ticks));
        var update = Builders<BsonDocument>.Update.Set(NextTryAtUtc, leaseEnd).Set(LeaseUntilUtc, leaseEnd);
        var options = new FindOneAndUpdateOptions<BsonDocument> { Sort = ClaimOrder, ReturnDocument = ReturnDocument.After };
        var messages = new List<T>(maxCount);
        var claimed = 0;
        while (claimed < maxCount && await documents.FindOneAndUpdateAsync(due, update, options, ct) is { } document)
        {
            claimed++;
            var message = MongoDbStorage.Read(() => read(document));
            if (message.IsError)
            {
                await SetAsideAsync(documents, document[Id], message.Error!, ct);
                continue;
            }

            messages.Add(message.Value);
        }

        var nextDueAtUtc = claimed < maxCount ? await NextDueAsync(documents, scope, ct) : nowUtc;
        return new ClaimResult<T>(messages, nextDueAtUtc);
    }

    /// <summary>The earliest due time of the pending documents of a scope, which includes the lease ends, or null.</summary>
    public static async Task<DateTime?> NextDueAsync(IMongoCollection<BsonDocument> documents, FilterDefinition<BsonDocument> scope, CancellationToken ct)
    {
        var earliest = await documents.Find(_filter.And(scope, _filter.Eq(Status, Pending)))
            .Sort(Builders<BsonDocument>.Sort.Ascending(NextTryAtTicks))
            .Project(Builders<BsonDocument>.Projection.Include(NextTryAtUtc))
            .Limit(1)
            .FirstOrDefaultAsync(ct);
        return earliest is null ? null : BsonDocuments.TimeOf(earliest["delivery"]["nextTryAtUtc"]);
    }

    /// <summary>Filters the documents of a query with the filters that the inbox and the outbox share, and with the cursor of the page (D112).</summary>
    public static FilterDefinition<BsonDocument> Filter(
        TenantKey key, IReadOnlyList<Guid>? ids, DeliveryStatus? status, DateTime? createdFromUtc, DateTime? createdToUtc,
        DateTime? dueBeforeUtc, bool includeTest, MessageCursor? after)
    {
        var filters = new List<FilterDefinition<BsonDocument>> { OfTenant(key.Tenant) };
        if (ids is not null)
        {
            filters.Add(_filter.In(MessageId, ids.Select(IdText)));
        }

        if (status is { } value)
        {
            filters.Add(_filter.Eq(Status, value.ToString()));
        }

        if (createdFromUtc is { } from)
        {
            filters.Add(_filter.Gte(CreatedAtTicks, from.Ticks));
        }

        if (createdToUtc is { } to)
        {
            filters.Add(_filter.Lt(CreatedAtTicks, to.Ticks));
        }

        if (dueBeforeUtc is { } dueBefore)
        {
            filters.Add(_filter.And(_filter.Eq(Status, Pending), _filter.Lt(NextTryAtTicks, dueBefore.Ticks)));
        }

        if (!includeTest)
        {
            filters.Add(_filter.Ne(Flags, Test));
        }

        if (after is { } cursor)
        {
            filters.Add(_filter.Or(
                _filter.Lt(CreatedAtTicks, cursor.CreatedAtUtc.Ticks),
                _filter.And(_filter.Eq(CreatedAtTicks, cursor.CreatedAtUtc.Ticks), _filter.Lt(MessageId, IdText(cursor.Id)))));
        }

        return _filter.And(filters);
    }

    /// <summary>Reads one page newest first: the time of the add descending, then the id descending (D112). The page reads no content.</summary>
    public static async Task<MessagePage<T>> PageAsync<T>(
        IMongoCollection<BsonDocument> documents, FilterDefinition<BsonDocument> filter, int limit, string[] content,
        Func<BsonDocument, T> summary, Func<T, DeliveryState> delivery, CancellationToken ct)
    {
        var page = await documents.Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending(CreatedAtTicks).Descending(MessageId))
            .Project(Builders<BsonDocument>.Projection.Combine(content.Select(static field => Builders<BsonDocument>.Projection.Exclude(field))))
            .Limit(limit)
            .ToListAsync(ct);
        List<T> items = [.. page.Select(summary)];
        return new MessagePage<T>(items, items.Count > 0 && items.Count == limit ? MessageCursor.Of(delivery(items[^1])) : null);
    }

    /// <summary>The documents of the ids of one tenant.</summary>
    public static FilterDefinition<BsonDocument> OfIds(TenantKey key, IReadOnlyList<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return _filter.And(OfTenant(key.Tenant), _filter.In(MessageId, ids.Select(IdText)));
    }

    /// <summary>The documents with no active lease at a time.</summary>
    public static FilterDefinition<BsonDocument> NoActiveLease(DateTime nowUtc) =>
        _filter.Or(_filter.Eq(LeaseUntilUtc, BsonNull.Value), _filter.Lte(_leaseUntilTicks, nowUtc.Ticks));

    /// <summary>Makes the failed and the pending documents of the ids due now, when no lease is active and the message has not expired (D111).</summary>
    public static async Task<int> RetryAsync(
        IMongoCollection<BsonDocument> documents, TenantKey key, IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct)
    {
        var filter = _filter.And(
            OfIds(key, ids),
            _filter.In(Status, [_failed, Pending]),
            NoActiveLease(nowUtc),
            _filter.Or(_filter.Eq("delivery.expiresAtUtc", BsonNull.Value), _filter.Gt(_expiresAtTicks, nowUtc.Ticks)));
        var retry = new BsonDocument("$set", new BsonDocument
        {
            {
                "delivery.maxTryCount", new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$eq", new BsonArray { "$delivery.status", _failed }),
                    new BsonDocument("$add", new BsonArray { "$delivery.tryCount", addedTries }),
                    "$delivery.maxTryCount",
                })
            },
            { Status, Literal(Pending) },
            { NextTryAtUtc, Literal(BsonDocuments.Time(nowUtc)) },
            { LeaseUntilUtc, BsonNull.Value },
        });
        var result = await documents.UpdateManyAsync(filter, Pipeline(retry), cancellationToken: ct);
        return (int)result.MatchedCount;
    }

    /// <summary>Deletes up to <paramref name="maxCount"/> finished documents that finished before a time, those that finished first first (D128).</summary>
    public static async Task<int> PurgeAsync(IMongoCollection<BsonDocument> documents, DateTime finishedBeforeUtc, int maxCount, CancellationToken ct)
    {
        var finished = _filter.In(Status, [nameof(DeliveryStatus.Done), _failed, nameof(DeliveryStatus.Cancelled)]);
        var finishedAt = new BsonDocument("$ifNull", new BsonArray { "$delivery.lastTryAtUtc.t", "$delivery.createdAtUtc.t" });
        var oldest = await documents.Aggregate()
            .Match(finished)
            .Project(new BsonDocument("finishedAt", finishedAt))
            .Match(Builders<BsonDocument>.Filter.Lt("finishedAt", finishedBeforeUtc.Ticks))
            .Sort(Builders<BsonDocument>.Sort.Ascending("finishedAt").Ascending(Id))
            .Limit(maxCount)
            .ToListAsync(ct);
        if (oldest.Count == 0)
        {
            return 0;
        }

        var deleted = await documents.DeleteManyAsync(_filter.And(_filter.In(Id, oldest.Select(static document => document[Id])), finished), ct);
        return (int)deleted.DeletedCount;
    }

    /// <summary>True for a document with the test flag, as an expression of a pipeline stage.</summary>
    public static readonly BsonDocument IsTestExpression = new("$eq", new BsonArray { "$flags", Test });

    /// <summary>The documents of a scope that match the filters of a traffic count (D164).</summary>
    public static FilterDefinition<BsonDocument> Traffic(FilterDefinition<BsonDocument> scope, TrafficQuery query)
    {
        var filters = new List<FilterDefinition<BsonDocument>> { scope };
        if (query.CreatedFromUtc is { } from)
        {
            filters.Add(_filter.Gte(CreatedAtTicks, from.Ticks));
        }

        if (query.CreatedToUtc is { } to)
        {
            filters.Add(_filter.Lt(CreatedAtTicks, to.Ticks));
        }

        if (query.Status is { } status)
        {
            filters.Add(_filter.Eq(Status, status.ToString()));
        }

        if (!query.IncludeTest)
        {
            filters.Add(_filter.Ne(Flags, Test));
        }

        return _filter.And(filters);
    }

    /// <summary>The group stage of a count: the number of documents of each key, and the earliest due time of the pending ones.</summary>
    public static BsonDocument CountGroup(BsonDocument key) =>
        new()
        {
            { Id, key },
            { "count", new BsonDocument("$sum", 1) },
            {
                "oldest", new BsonDocument("$min", new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$eq", new BsonArray { "$delivery.status", Pending }), "$delivery.nextTryAtUtc.t", BsonNull.Value,
                }))
            },
        };

    /// <summary>The earliest due time of the pending documents of a count group, or null.</summary>
    public static DateTime? OldestOf(BsonDocument group) =>
        group["oldest"].IsBsonNull ? null : new DateTime(group["oldest"].AsInt64, DateTimeKind.Utc);

    public static async Task<int> DeleteTestDataAsync(IMongoCollection<BsonDocument> documents, CancellationToken ct) =>
        (int)(await documents.DeleteManyAsync(_filter.Eq(Flags, Test), ct)).DeletedCount;

    /// <summary>A pipeline update of one stage, for a change that reads the stored fields.</summary>
    public static UpdateDefinition<BsonDocument> Pipeline(BsonDocument stage) =>
        Builders<BsonDocument>.Update.Pipeline(PipelineDefinition<BsonDocument, BsonDocument>.Create([stage]));

    /// <summary>A value that a pipeline stage takes as it is, also a text that starts with <c>$</c>.</summary>
    public static BsonDocument Literal(BsonValue value) => new("$literal", value);

    private static async Task SetAsideAsync(IMongoCollection<BsonDocument> documents, BsonValue id, Error error, CancellationToken ct)
    {
        if (error.Type.IsTransient)
        {
            return;
        }

        await documents.UpdateOneAsync(
            _filter.Eq(Id, id),
            Builders<BsonDocument>.Update
                .Set(Status, _failed)
                .Set(LeaseUntilUtc, BsonNull.Value)
                .Set(LastError, $"{error.Type.Name}: {error.Message}"),
            cancellationToken: ct);
    }
}
