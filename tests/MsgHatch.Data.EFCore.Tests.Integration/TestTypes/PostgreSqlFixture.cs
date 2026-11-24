using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// One PostgreSQL for the test assembly (D180): the server of <see cref="PostgreSqlIntegration"/>, for example a local
/// PostgreSQL, or else PostgreSQL in a container through Testcontainers (D124). Each test class uses its own database. Each
/// database name starts with the prefix of the test project, so the fixture touches no other database: it drops these
/// databases at the start of a run, for example after a crashed run, and at the end of the run. A database is created
/// again at its first use. A machine with no image of the Ryuk container of Testcontainers sets
/// <c>TESTCONTAINERS_RYUK_DISABLED=true</c> for its run (D177).
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public const string Image = "postgres:18.7";

    /// <summary>PostgreSQL keeps at most 63 bytes of a database name, and cuts a longer name with no error.</summary>
    public const int MaxDatabaseNameBytes = 63;

    /// <summary>The maximum count of connections of each pool of a test database.</summary>
    public const int MaxPoolSize = 10;

    private const string _testDatabases = "SELECT datname FROM pg_database WHERE left(datname, length(@prefix)) = @prefix";

    private readonly string _prefix = PrefixOf(Assembly.GetEntryAssembly()!.GetName().Name!);

    private readonly ConcurrentDictionary<string, PostgreSqlTestDatabase> _databases = new(StringComparer.Ordinal);

    private PostgreSqlContainer? _container;

    private string _server = string.Empty;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The prefix of the database names of a test project: its name in lower case, with no <c>.Tests.Integration</c>, each
    /// <c>.</c> as <c>_</c>, and <c>__</c> at the end. So a name of the longest test project keeps 16 bytes for the name
    /// of its database, and the prefix of one project is never the start of the prefix of another project.
    /// </summary>
    public static string PrefixOf(string testProject)
    {
        ArgumentNullException.ThrowIfNull(testProject);
        return $"{testProject.Replace(".Tests.Integration", string.Empty, StringComparison.Ordinal).Replace('.', '_').ToLowerInvariant()}__";
    }

    public async ValueTask InitializeAsync()
    {
        if (PostgreSqlIntegration.IsConfigured)
        {
            _server = PostgreSqlIntegration.Settings.ConnectionString!;
        }
        else
        {
            _container = new PostgreSqlBuilder(Image).Build();
            await _container.StartAsync(Ct);
            _server = _container.GetConnectionString();
        }

        var (version, dropped) = await DropTestDatabasesAsync(Ct);
        TestContext.Current.SendDiagnosticMessage(
            $"PostgreSQL {version} from {(_container is null ? "PostgreSqlIntegration:ConnectionString" : Image)}: dropped {dropped} test databases with the prefix {_prefix} at the start.");
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        var (_, dropped) = await DropTestDatabasesAsync(CancellationToken.None);
        TestContext.Current.SendDiagnosticMessage($"Dropped {dropped} test databases with the prefix {_prefix} at the end.");
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>The database of one name, the same instance for each call with that name, with the create step of its first call.</summary>
    public PostgreSqlTestDatabase Database(string name, Func<PostgreSqlTestDatabase, CancellationToken, Task>? create = null) =>
        _databases.GetOrAdd(
            name, static (name, state) => new PostgreSqlTestDatabase(state.Fixture.ConnectionStringOf(state.Fixture.DatabaseNameOf(name)), state.Create),
            (Fixture: this, Create: create));

    private string DatabaseNameOf(string name)
    {
        var database = $"{_prefix}{name}";
        return Encoding.UTF8.GetByteCount(database) <= MaxDatabaseNameBytes
            ? database
            : throw new ArgumentException($"The database name {database} has more than {MaxDatabaseNameBytes} bytes.", nameof(name));
    }

    /// <summary>
    /// The connection string of a database, with a pool of at most <see cref="MaxPoolSize"/> connections. PostgreSQL allows
    /// 100 connections by default, and each test database has its own pools, so the default pool of 100 connections of
    /// Npgsql makes the parallel tests fail with the error 53300.
    /// </summary>
    private string ConnectionStringOf(string database) =>
        new NpgsqlConnectionStringBuilder(_server) { Database = database, MaxPoolSize = MaxPoolSize }.ConnectionString;

    /// <summary>Drops each database whose name starts with the prefix of the test project, and gives the version of the server.</summary>
    private async Task<(string Version, int Dropped)> DropTestDatabasesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringOf("postgres"));
        await connection.OpenAsync(ct);
        var names = new List<string>();
        await using (var select = new NpgsqlCommand(_testDatabases, connection))
        {
            select.Parameters.AddWithValue("prefix", _prefix);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        foreach (var name in names)
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {PostgreSqlTestDatabase.Quote(name)} WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync(ct);
        }

        return (connection.PostgreSqlVersion.ToString(), names.Count);
    }
}
