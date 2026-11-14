using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Data.MongoDb;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Data.MongoDb;

/// <summary>
/// The subscription store on MongoDB (section 4.5, D78, D121): one document of the collection
/// <c>MsgHatchSubscription</c> for each subscription, with its id as <c>_id</c>, its fields with camelCase names, its
/// times as <c>{ d, t }</c> (D185), and its channel settings as the sub-document <c>settings</c> converted from the JSON of
/// <see cref="ChannelSettingsJson"/> (D149, D184). The store of one tenant key reads and writes only the documents of its
/// tenant. A unique index on the tenant, the message type, the owner, the channel, and the recipient kind stops a
/// duplicate, and a missing value is null, which the index treats as one value. A second subscription with the same key,
/// or with a stored id, gives <see cref="Errors.Conflict"/>.
/// </summary>
public sealed class MongoDbSubscriptionStore : ISubscriptionStore
{
    /// <summary>The name of the collection.</summary>
    public const string CollectionName = "MsgHatchSubscription";

    private const string _id = "_id";
    private const string _tenant = "tenant";
    private const string _type = "type";
    private const string _level = "level";
    private const string _company = "company";
    private const string _user = "user";
    private const string _revision = "revision";
    private const string _settings = "settings";
    private const string _namedUsers = "namedUsers";

    private static readonly FilterDefinitionBuilder<BsonDocument> _filter = Builders<BsonDocument>.Filter;

    private readonly MongoDbStorage _storage;
    private readonly BsonValue _tenantId;
    private readonly ChannelSettingsJson _settingsJson;

    /// <summary>Makes the store of one tenant key on a database.</summary>
    /// <param name="database">The database of the stores.</param>
    /// <param name="key">The tenant key of the store.</param>
    /// <param name="settingsJson">The JSON of the channel settings.</param>
    public MongoDbSubscriptionStore(IMongoDatabase database, TenantKey key, ChannelSettingsJson settingsJson)
    {
        ArgumentNullException.ThrowIfNull(settingsJson);
        _storage = new MongoDbStorage(
            database,
            CollectionName,
            [
                new CreateIndexModel<BsonDocument>(
                    new BsonDocument
                    {
                        { _tenant, 1 }, { _type, 1 }, { _level, 1 }, { _company, 1 }, { _user, 1 }, { "channel", 1 }, { "recipients", 1 },
                    },
                    new CreateIndexOptions { Name = "UX_MsgHatchSubscription_Owner", Unique = true }),
            ]);
        _tenantId = BsonDocuments.Text(key.Tenant?.Value);
        _settingsJson = settingsJson;
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
        MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(users);
        return ReadAllAsync(
            _filter.Eq(_type, type.Name) & _filter.Or(
                _filter.Eq(_level, nameof(SubscriptionLevel.Default)),
                _filter.Eq(_level, nameof(SubscriptionLevel.Company)) & _filter.In(_company, companies.Select(static company => company.Value)),
                _filter.Eq(_level, nameof(SubscriptionLevel.User)) & _filter.In(_user, users.Select(static user => user.Value))),
            ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = _filter.Empty;
        filter = query.Type is { } type ? filter & _filter.Eq(_type, type.Name) : filter;
        filter = query.Company is { } company ? filter & _filter.Eq(_company, company.Value) : filter;
        filter = query.User is { } user ? filter & _filter.Eq(_user, user.Value) : filter;
        filter = query.Level is { } level ? filter & _filter.Eq(_level, level.ToString()) : filter;
        return ReadAllAsync(filter, ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct) =>
        _storage.RunAsync<Subscription?>(
            async () =>
                await _storage.Documents.Find(OfTenant(_filter.Eq(_id, id.ToString("D")))).FirstOrDefaultAsync(ct) is { } document
                    ? Read(document).ThenIfSuccess(static subscription => ErrorOr<Subscription?>.ForValue(subscription))
                    : ErrorOr<Subscription?>.ForValue(null),
            ct);

    /// <inheritdoc />
    public ValueTask<Error?> AddAsync(Subscription subscription, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var document = DocumentOf(subscription);
        return document.IsError
            ? ValueTask.FromResult<Error?>(document.Error)
            : _storage.RunAsync(
                async () =>
                {
                    try
                    {
                        await _storage.Documents.InsertOneAsync(document.Value, cancellationToken: ct);
                        return null;
                    }
                    catch (MongoException exception) when (MongoDbStorage.IsDuplicateKey(exception))
                    {
                        return SameIdOrOwnerTypeChannelAndRecipients();
                    }
                },
                ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var document = DocumentOf(subscription);
        return document.IsError ? ValueTask.FromResult<Error?>(document.Error) : ReplaceAsync(document.Value, expectedRevision, ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct) =>
        _storage.RunAsync(
            async () =>
            {
                await _storage.Documents.DeleteOneAsync(OfTenant(_filter.Eq(_id, id.ToString("D"))), ct);
                return (Error?)null;
            },
            ct);

    /// <summary>
    /// Replaces the document of the tenant with the id and the expected revision. No matched document gives
    /// <see cref="Errors.Conflict"/> for a stored id, and <see cref="Errors.NotFound"/> else. A change into the key of
    /// another document gives <see cref="Errors.Conflict"/>.
    /// </summary>
    private ValueTask<Error?> ReplaceAsync(BsonDocument document, int expectedRevision, CancellationToken ct) =>
        _storage.RunAsync(
            async () =>
            {
                var id = _filter.Eq(_id, document[_id]);
                try
                {
                    var replaced = await _storage.Documents.ReplaceOneAsync(OfTenant(id & _filter.Eq(_revision, expectedRevision)), document, cancellationToken: ct);
                    return replaced.MatchedCount > 0 ? null
                        : await _storage.Documents.CountDocumentsAsync(OfTenant(id), cancellationToken: ct) > 0
                            ? new Error(Errors.Conflict, "The stored revision is not the expected one.")
                            : new Error(Errors.NotFound, "The subscription is not stored.");
                }
                catch (MongoException exception) when (MongoDbStorage.IsDuplicateKey(exception))
                {
                    return SameIdOrOwnerTypeChannelAndRecipients();
                }
            },
            ct);

    private FilterDefinition<BsonDocument> OfTenant(FilterDefinition<BsonDocument> filter) => _filter.Eq(_tenant, _tenantId) & filter;

    private ValueTask<ErrorOr<IReadOnlyList<Subscription>>> ReadAllAsync(FilterDefinition<BsonDocument> filter, CancellationToken ct) =>
        _storage.RunAsync<IReadOnlyList<Subscription>>(
            async () =>
            {
                var documents = await _storage.Documents.Find(OfTenant(filter)).ToListAsync(ct);
                var subscriptions = new List<Subscription>(documents.Count);
                foreach (var document in documents)
                {
                    var subscription = Read(document);
                    if (subscription.IsError)
                    {
                        return subscription.Error!;
                    }

                    subscriptions.Add(subscription.Value);
                }

                return subscriptions;
            },
            ct);

    private ErrorOr<BsonDocument> DocumentOf(Subscription subscription)
    {
        SerializedSettings? settings = null;
        if (subscription.Settings is { } value)
        {
            var serialized = _settingsJson.Serialize(subscription.Channel.GetValueOrDefault(), value);
            if (serialized.IsError)
            {
                return serialized.Error!;
            }

            settings = serialized.Value;
        }

        return MongoDbStorage.Read(() => ErrorOr<BsonDocument>.ForValue(new BsonDocument
        {
            { _id, subscription.Id.ToString("D") },
            { _revision, subscription.Revision },
            { _tenant, _tenantId },
            { _level, subscription.Owner.Level.ToString() },
            { _company, BsonDocuments.Text(subscription.Owner.Company?.Value) },
            { _user, BsonDocuments.Text(subscription.Owner.User?.Value) },
            { _type, subscription.Type.Name },
            { "kind", subscription.Kind.ToString() },
            { "channel", BsonDocuments.Text(subscription.Channel?.Name) },
            { "settingsType", BsonDocuments.Text(settings?.ContentType) },
            { "settingsVersion", settings is null ? BsonNull.Value : new BsonInt32(settings.Version) },
            { _settings, settings is null ? BsonNull.Value : BsonDocuments.ValueOfJson(settings.Json) },
            { "recipients", subscription.Recipients.Kind.ToString() },
            {
                _namedUsers, subscription.Recipients.Kind == RecipientKind.NamedUsers
                    ? new BsonArray(subscription.Recipients.Users.Select(static user => user.Value))
                    : BsonNull.Value
            },
            { "mandatory", subscription.Mandatory },
            { "createdAtUtc", BsonDocuments.Time(subscription.CreatedAtUtc) },
            { "lastModifiedAtUtc", BsonDocuments.Time(subscription.LastModifiedAtUtc) },
        }));
    }

    /// <summary>Reads a stored document. Each caller runs it inside the boundary of the storage, which turns an invalid value into an error.</summary>
    /// <exception cref="InvalidCastException">A field has another BSON type.</exception>
    /// <exception cref="ArgumentException">A field has an invalid name, id, or enum value.</exception>
    private ErrorOr<Subscription> Read(BsonDocument document)
    {
        ChannelSettings? settings = null;
        if (!document[_settings].IsBsonNull)
        {
            var read = _settingsJson.Deserialize(
                document["settingsType"].AsString, document["settingsVersion"].AsInt32, BsonDocuments.ToJsonText(document[_settings]));
            if (read.IsError)
            {
                return read.Error!;
            }

            settings = read.Value;
        }

        return new Subscription(
            Guid.Parse(document[_id].AsString),
            document[_revision].AsInt32,
            OwnerOf(Enum.Parse<SubscriptionLevel>(document[_level].AsString), BsonDocuments.NullableTextOf(document[_company]),
                BsonDocuments.NullableTextOf(document[_user])),
            new MessageType(document[_type].AsString),
            Enum.Parse<SubscriptionKind>(document["kind"].AsString),
            BsonDocuments.NullableTextOf(document["channel"]) is { } channel ? new ChannelId(channel) : null,
            settings,
            RecipientsOf(Enum.Parse<RecipientKind>(document["recipients"].AsString), document[_namedUsers]),
            document["mandatory"].AsBoolean,
            BsonDocuments.TimeOf(document["createdAtUtc"]),
            BsonDocuments.TimeOf(document["lastModifiedAtUtc"]));
    }

    /// <exception cref="ArgumentException">The owner ids do not match the level.</exception>
    private static SubscriptionOwner OwnerOf(SubscriptionLevel level, string? company, string? user) => (level, company, user) switch
    {
        (SubscriptionLevel.Default, null, null) => SubscriptionOwner.Default,
        (SubscriptionLevel.Company, { } id, null) => SubscriptionOwner.ForCompany(new CompanyId(id)),
        (SubscriptionLevel.User, null, { } id) => SubscriptionOwner.ForUser(new UserId(id)),
        _ => throw new ArgumentException("A subscription owner has the level Default, Company, or User, and only the id of its level."),
    };

    /// <exception cref="InvalidCastException">The named users are not an array of texts.</exception>
    private static SubscriptionRecipients RecipientsOf(RecipientKind kind, BsonValue namedUsers) => kind switch
    {
        RecipientKind.NamedUsers => SubscriptionRecipients.ForUsers([.. namedUsers.AsBsonArray.Select(static user => new UserId(user.AsString))]),
        RecipientKind.Company => SubscriptionRecipients.Company,
        _ => SubscriptionRecipients.ScopeUsers,
    };

    private static Error SameIdOrOwnerTypeChannelAndRecipients() =>
        new(Errors.Conflict, "A subscription has the id, or the owner already has a subscription for the message type, the channel, and the recipient kind.");
}
