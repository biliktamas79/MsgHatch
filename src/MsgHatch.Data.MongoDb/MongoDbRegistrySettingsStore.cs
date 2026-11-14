using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Data.MongoDb;

/// <summary>
/// The registry settings on MongoDB (section 4.20): one document of <c>MsgHatchChannelEntry</c> for each channel entry,
/// and one document of <c>MsgHatchMessageTypeEntry</c> for each message type entry, with the name as <c>_id</c> and
/// <c>disabledChannels</c> as an array of names. A new entry is an insert, and a name that is already stored gives
/// <see cref="Errors.Conflict"/>. A change is an update with the expected revision, and no matched document gives
/// <see cref="Errors.Conflict"/>.
/// </summary>
public sealed class MongoDbRegistrySettingsStore : IRegistrySettingsStore
{
    /// <summary>The name of the collection of the channel entries.</summary>
    public const string ChannelEntryCollectionName = "MsgHatchChannelEntry";

    /// <summary>The name of the collection of the message type entries.</summary>
    public const string MessageTypeEntryCollectionName = "MsgHatchMessageTypeEntry";

    private const string _id = "_id";
    private const string _revision = "revision";

    private static readonly FilterDefinitionBuilder<BsonDocument> _filter = Builders<BsonDocument>.Filter;

    private readonly MongoDbStorage _channels;
    private readonly IMongoCollection<BsonDocument> _messageTypes;

    /// <summary>Makes the store on a database.</summary>
    /// <param name="database">The database of the stores.</param>
    public MongoDbRegistrySettingsStore(IMongoDatabase database)
    {
        _channels = new MongoDbStorage(database, ChannelEntryCollectionName, indexes: []);
        _messageTypes = database.GetCollection<BsonDocument>(MessageTypeEntryCollectionName);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct) =>
        _channels.RunAsync<RegistryEntries>(
            async () =>
            {
                var channels = await _channels.Documents.Find(_filter.Empty).ToListAsync(ct);
                var messageTypes = await _messageTypes.Find(_filter.Empty).ToListAsync(ct);
                return new RegistryEntries(
                    [.. channels.Select(ChannelEntryOf).OrderBy(static entry => entry.Channel.Name, StringComparer.Ordinal)],
                    [.. messageTypes.Select(MessageTypeEntryOf).OrderBy(static entry => entry.Type.Name, StringComparer.Ordinal)]);
            },
            ct);

    /// <inheritdoc />
    public ValueTask<Error?> SaveChannelEntryAsync(ChannelEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return SaveAsync(
            _channels.Documents,
            entry.Revision,
            new BsonDocument
            {
                { _id, entry.Channel.Name },
                { "displayName", BsonDocuments.Text(entry.DisplayName) },
                { "description", BsonDocuments.Text(entry.Description) },
                { "enabled", entry.Enabled },
                { _revision, entry.Revision },
                { "lastModifiedAtUtc", BsonDocuments.Time(entry.LastModifiedAtUtc) },
            },
            ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> SaveMessageTypeEntryAsync(MessageTypeEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return SaveAsync(
            _messageTypes,
            entry.Revision,
            new BsonDocument
            {
                { _id, entry.Type.Name },
                { "displayName", BsonDocuments.Text(entry.DisplayName) },
                { "description", BsonDocuments.Text(entry.Description) },
                { "enabled", entry.Enabled },
                { "disabledChannels", new BsonArray(entry.DisabledChannels.Select(static channel => channel.Name)) },
                { _revision, entry.Revision },
                { "lastModifiedAtUtc", BsonDocuments.Time(entry.LastModifiedAtUtc) },
            },
            ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> DeleteChannelEntryAsync(ChannelId channel, CancellationToken ct) => DeleteAsync(_channels.Documents, channel.Name, ct);

    /// <inheritdoc />
    public ValueTask<Error?> DeleteMessageTypeEntryAsync(MessageType type, CancellationToken ct) => DeleteAsync(_messageTypes, type.Name, ct);

    /// <summary>
    /// Writes an entry: the revision 1 is an insert, and a stored name gives <see cref="Errors.Conflict"/>, so of two
    /// writers only one creates the entry. Another revision replaces the entry with the revision before it.
    /// </summary>
    private ValueTask<Error?> SaveAsync(IMongoCollection<BsonDocument> documents, int revision, BsonDocument entry, CancellationToken ct) =>
        _channels.RunAsync(
            async () =>
            {
                if (revision == 1)
                {
                    try
                    {
                        await documents.InsertOneAsync(entry, cancellationToken: ct);
                        return null;
                    }
                    catch (MongoException exception) when (MongoDbStorage.IsDuplicateKey(exception))
                    {
                        return StoredRevisionIsNotThePrevious();
                    }
                }

                var replaced = await documents.ReplaceOneAsync(
                    _filter.And(_filter.Eq(_id, entry[_id]), _filter.Eq(_revision, revision - 1)), entry, cancellationToken: ct);
                return replaced.MatchedCount == 1 ? null : StoredRevisionIsNotThePrevious();
            },
            ct);

    private ValueTask<Error?> DeleteAsync(IMongoCollection<BsonDocument> documents, string name, CancellationToken ct) =>
        _channels.RunAsync(
            async () =>
            {
                await documents.DeleteOneAsync(_filter.Eq(_id, name), ct);
                return (Error?)null;
            },
            ct);

    private static Error StoredRevisionIsNotThePrevious() =>
        new(Errors.Conflict, "The stored revision is not the revision before the revision of the entry.");

    /// <exception cref="ArgumentException">The document has an invalid channel name.</exception>
    /// <exception cref="InvalidCastException">A field has another BSON type.</exception>
    private static ChannelEntry ChannelEntryOf(BsonDocument document) =>
        new(
            new ChannelId(document[_id].AsString), BsonDocuments.NullableTextOf(document["displayName"]),
            BsonDocuments.NullableTextOf(document["description"]), document["enabled"].AsBoolean, document[_revision].AsInt32,
            BsonDocuments.TimeOf(document["lastModifiedAtUtc"]));

    /// <exception cref="ArgumentException">The document has an invalid name.</exception>
    /// <exception cref="InvalidCastException">A field has another BSON type.</exception>
    private static MessageTypeEntry MessageTypeEntryOf(BsonDocument document) =>
        new(
            new MessageType(document[_id].AsString), BsonDocuments.NullableTextOf(document["displayName"]),
            BsonDocuments.NullableTextOf(document["description"]), document["enabled"].AsBoolean,
            [.. document["disabledChannels"].AsBsonArray.Select(static channel => new ChannelId(channel.AsString))], document[_revision].AsInt32,
            BsonDocuments.TimeOf(document["lastModifiedAtUtc"]));
}
