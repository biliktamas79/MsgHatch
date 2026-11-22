using System.Collections.Concurrent;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Driver;
using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using Testcontainers.MongoDb;

[assembly: AssemblyFixture(typeof(MongoDbFixture))]

namespace MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;

/// <summary>
/// One MongoDB for the test assembly (D183): the server of <see cref="MongoDbIntegration"/>, for example the local
/// container dev-mongo, or else MongoDB in a container through Testcontainers (D124), as a replica set with one member,
/// because the change stream of the dispatch signal needs one (D303). Each test class uses its own database. Each
/// database name starts with the name of the test project, so the fixture touches no other database: it drops these
/// databases at the start of a run, for example after a crashed run, and at the end of the run. MongoDB creates a
/// database again at its first write. A machine with no image of the Ryuk container of Testcontainers sets
/// <c>TESTCONTAINERS_RYUK_DISABLED=true</c> for its run (D177).
/// </summary>
public sealed class MongoDbFixture : IAsyncLifetime
{
    public const string Image = "mongo:8.3";

    private readonly string _prefix = $"{Assembly.GetEntryAssembly()!.GetName().Name!.Replace('.', '_')}_";

    private readonly ConcurrentDictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    private MongoDbContainer? _container;

    private MongoClient? _client;

    /// <summary>The connection string of the server.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (MongoDbIntegration.IsConfigured)
        {
            ConnectionString = MongoDbIntegration.Settings.ConnectionString!;
        }
        else
        {
            _container = new MongoDbBuilder(Image).WithReplicaSet().Build();
            await _container.StartAsync(Ct);
            ConnectionString = _container.GetConnectionString();
        }

        _client = new MongoClient(ConnectionString);
        var dropped = await DropTestDatabasesAsync(Ct);
        var buildInfo = await _client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1), cancellationToken: Ct);
        TestContext.Current.SendDiagnosticMessage(
            $"MongoDB {buildInfo["version"]} from {(_container is null ? "MongoDbIntegration:ConnectionString" : Image)}: dropped {dropped} test databases with the prefix {_prefix} at the start.");
    }

    public async ValueTask DisposeAsync()
    {
        var dropped = await DropTestDatabasesAsync(CancellationToken.None);
        TestContext.Current.SendDiagnosticMessage($"Dropped {dropped} test databases with the prefix {_prefix} at the end.");
        _client?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>The database of one name, the same instance for each call with that name.</summary>
    public TestDatabase Database(string name) =>
        _databases.GetOrAdd(name, static (name, fixture) => new TestDatabase(fixture._client!.GetDatabase($"{fixture._prefix}{name}")), this);

    /// <summary>Drops each database whose name starts with the prefix of the test project.</summary>
    private async Task<int> DropTestDatabasesAsync(CancellationToken ct)
    {
        var names = await (await _client!.ListDatabaseNamesAsync(ct)).ToListAsync(ct);
        string[] testDatabases = [.. names.Where(name => name.StartsWith(_prefix, StringComparison.Ordinal))];
        foreach (var name in testDatabases)
        {
            await _client.DropDatabaseAsync(name, ct);
        }

        return testDatabases.Length;
    }
}
