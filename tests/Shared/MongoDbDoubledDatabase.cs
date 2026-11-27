using System.Collections.Concurrent;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NSubstitute;

namespace MsgHatch.Tests.Shared;

/// <summary>
/// A database of test doubles for the unit tests of the MongoDB stores (D330). Each collection is an NSubstitute double, so
/// no call reaches a server, and no client starts a server monitor. Each instance has a database name of its own, because a
/// store creates the indexes of a collection one time for each database in the process. <c>Directory.Build.props</c> links
/// this file into the unit test projects of <c>MsgHatch.Data.MongoDb</c> and <c>MsgHatch.Subscriptions.Data.MongoDb</c>.
/// </summary>
internal sealed class DoubledDatabase
{
    private static readonly RenderArgs<BsonDocument> _renderArgs = new(BsonDocumentSerializer.Instance, BsonSerializer.SerializerRegistry);

    private readonly ConcurrentDictionary<string, IMongoCollection<BsonDocument>> _collections = new(StringComparer.Ordinal);

    public DoubledDatabase()
    {
        var client = Substitute.For<IMongoClient>();
        client.Settings.Returns(new MongoClientSettings());
        Database = Substitute.For<IMongoDatabase>();
        Database.Client.Returns(client);
        Database.DatabaseNamespace.Returns(new DatabaseNamespace($"unit{Guid.NewGuid():N}"));
        Database.GetCollection<BsonDocument>(Arg.Any<string>(), Arg.Any<MongoCollectionSettings?>()).Returns(call => Collection(call.ArgAt<string>(0)));
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<BsonDocument> Collection(string name) => _collections.GetOrAdd(name, static _ => Substitute.For<IMongoCollection<BsonDocument>>());

    /// <summary>Makes each find of a collection give the documents, as one batch.</summary>
    public void Finds(string collection, params BsonDocument[] documents) =>
        Collection(collection)
            .FindAsync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<FindOptions<BsonDocument, BsonDocument>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IAsyncCursor<BsonDocument>>(new DocumentCursor(documents)));

    /// <summary>Makes each aggregation of a collection give the documents, as one batch.</summary>
    public void Aggregates(string collection, params BsonDocument[] documents) =>
        Collection(collection)
            .AggregateAsync(Arg.Any<PipelineDefinition<BsonDocument, BsonDocument>>(), Arg.Any<AggregateOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IAsyncCursor<BsonDocument>>(new DocumentCursor(documents)));

    /// <summary>The arguments of each call of a method of a collection, in the order of the calls.</summary>
    public IReadOnlyList<object?[]> CallsOf(string collection, string method) =>
        [.. Collection(collection).ReceivedCalls().Where(call => call.GetMethodInfo().Name == method).Select(static call => call.GetArguments())];

    /// <summary>The documents of each <c>InsertManyAsync</c> of a collection, in the order of the calls.</summary>
    public IReadOnlyList<IReadOnlyList<BsonDocument>> InsertsOf(string collection) =>
        [.. CallsOf(collection, nameof(IMongoCollection<>.InsertManyAsync)).Select(static arguments => (IReadOnlyList<BsonDocument>)[.. (IEnumerable<BsonDocument>)arguments[0]!])];

    /// <summary>The filter and the options of each <c>FindAsync</c> of a collection, in the order of the calls.</summary>
    public IReadOnlyList<(FilterDefinition<BsonDocument> Filter, FindOptions<BsonDocument, BsonDocument> Options)> FindsOf(string collection) =>
        [.. CallsOf(collection, nameof(IMongoCollection<>.FindAsync)).Select(static arguments =>
            ((FilterDefinition<BsonDocument>)arguments[0]!, (FindOptions<BsonDocument, BsonDocument>)arguments[1]!))];

    /// <summary>The stages of each <c>AggregateAsync</c> of a collection, in the order of the calls.</summary>
    public IReadOnlyList<IReadOnlyList<BsonDocument>> AggregationsOf(string collection) =>
        [.. CallsOf(collection, nameof(IMongoCollection<>.AggregateAsync)).Select(static arguments =>
            (IReadOnlyList<BsonDocument>)[.. ((PipelineDefinition<BsonDocument, BsonDocument>)arguments[0]!).Render(_renderArgs).Documents])];

    public static BsonDocument Rendered(FilterDefinition<BsonDocument> filter) => filter.Render(_renderArgs);

    public static BsonDocument Rendered(SortDefinition<BsonDocument> sort) => sort.Render(_renderArgs);

    public static BsonDocument Rendered(ProjectionDefinition<BsonDocument, BsonDocument> projection) => projection.Render(_renderArgs).Document;

    public static BsonDocument Rendered(UpdateDefinition<BsonDocument> update) => update.Render(_renderArgs).AsBsonDocument;

    /// <summary>A cursor of one batch.</summary>
    private sealed class DocumentCursor(IEnumerable<BsonDocument> documents) : IAsyncCursor<BsonDocument>
    {
        private bool _read;

        public IEnumerable<BsonDocument> Current => documents;

        public bool MoveNext(CancellationToken cancellationToken = default)
        {
            var first = !_read;
            _read = true;
            return first;
        }

        public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(MoveNext(cancellationToken));

        public void Dispose()
        {
        }
    }
}
