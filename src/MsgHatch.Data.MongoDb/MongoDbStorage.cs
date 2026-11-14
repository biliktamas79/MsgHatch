using System.Collections.Concurrent;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.MongoDb;

/// <summary>
/// The boundary of the database operations of a MongoDB store on one collection. Before its first operation, it creates
/// the indexes of the collection, one time for each database in the process. An operation returns an
/// <see cref="Error"/> in place of an exception: a server failure gives <see cref="Errors.Unavailable"/>, a stored value
/// that a person changed into an invalid value gives <see cref="Errors.SerializationFailed"/>, and a cancelled token
/// gives <see cref="Errors.OperationCancelled"/>.
/// </summary>
internal sealed class MongoDbStorage
{
    private static readonly ConcurrentDictionary<string, Task> _indexedCollections = new(StringComparer.Ordinal);

    private readonly IReadOnlyList<CreateIndexModel<BsonDocument>> _indexes;
    private readonly string _indexKey;

    /// <summary>Makes the boundary of one collection.</summary>
    /// <param name="database">The database.</param>
    /// <param name="collection">The name of the collection.</param>
    /// <param name="indexes">The indexes of the collection.</param>
    public MongoDbStorage(IMongoDatabase database, string collection, IReadOnlyList<CreateIndexModel<BsonDocument>> indexes)
    {
        ArgumentNullException.ThrowIfNull(database);
        Database = database;
        Documents = database.GetCollection<BsonDocument>(collection);
        _indexes = indexes;
        _indexKey = $"{string.Join(',', database.Client.Settings.Servers)}/{database.DatabaseNamespace.DatabaseName}.{collection}";
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<BsonDocument> Documents { get; }

    public async ValueTask<ErrorOr<T>> RunAsync<T>(Func<ValueTask<ErrorOr<T>>> operation, CancellationToken ct)
    {
        try
        {
            await CreateIndexesOnceAsync(ct);
            return await operation();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new Error(Errors.OperationCancelled);
        }
        catch (Exception exception) when (ErrorOf(exception) is { } error)
        {
            return error;
        }
    }

    public async ValueTask<Error?> RunAsync(Func<ValueTask<Error?>> operation, CancellationToken ct)
    {
        var result = await RunAsync<bool>(async () => await operation() is { } error ? error : true, ct);
        return result.Error;
    }

    /// <summary>Reads one stored document, and gives the error of that document in place of an exception.</summary>
    public static ErrorOr<T> Read<T>(Func<ErrorOr<T>> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (ErrorOf(exception) is { } error)
        {
            return error;
        }
    }

    /// <summary>
    /// Inserts the documents with one unordered <c>insertMany</c> and no transaction, and skips each document whose unique
    /// key is stored, so a second add of a key changes nothing, also from a parallel caller (D186).
    /// </summary>
    public static async Task InsertNewAsync(IMongoCollection<BsonDocument> documents, IReadOnlyList<BsonDocument> batch, CancellationToken ct)
    {
        try
        {
            await documents.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = false }, ct);
        }
        catch (MongoException exception) when (IsDuplicateKey(exception))
        {
            return;
        }
    }

    /// <summary>Whether a write failed only because a document with the same unique key is stored.</summary>
    public static bool IsDuplicateKey(MongoException exception) => exception switch
    {
        MongoWriteException write => write.WriteError?.Category == ServerErrorCategory.DuplicateKey && write.WriteConcernError is null,
        MongoBulkWriteException bulk => bulk.WriteErrors.All(static error => error.Category == ServerErrorCategory.DuplicateKey)
            && bulk.WriteConcernError is null,
        _ => false,
    };

    private async Task CreateIndexesOnceAsync(CancellationToken ct)
    {
        if (_indexes.Count == 0)
        {
            return;
        }

        var creation = _indexedCollections.GetOrAdd(_indexKey, static (_, storage) => storage.CreateIndexesAsync(), this);
        try
        {
            await creation.WaitAsync(ct);
        }
        catch when (creation.IsFaulted || creation.IsCanceled)
        {
            _indexedCollections.TryRemove(KeyValuePair.Create(_indexKey, creation));
            throw;
        }
    }

    private async Task CreateIndexesAsync() => await Documents.Indexes.CreateManyAsync(_indexes, CancellationToken.None);

    private static Error? ErrorOf(Exception exception) => exception switch
    {
        MongoException or TimeoutException => new Error(Errors.Unavailable, exception.Message, exception),
        FormatException or ArgumentException or JsonException or InvalidCastException or KeyNotFoundException or BsonException =>
            new Error(Errors.SerializationFailed, exception.Message, exception),
        _ => null,
    };
}
