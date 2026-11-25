using System.Collections.Concurrent;
using System.Reflection;
using MySqlConnector;
using Testcontainers.MySql;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// One MySQL for the test assembly (D180): the server of <see cref="MySqlIntegration"/>, for example the local container
/// dev-mysql, or else MySQL in a container through Testcontainers (D124). Each test class uses its own database, which is a
/// schema of MySQL. Each database name starts with the prefix of the test project (<see cref="PrefixOf"/>), so the fixture
/// touches no other database: it drops these databases at the start of a run, for example after a crashed run, and at the
/// end of the run. A database is created again at its first use.
/// </summary>
public sealed class MySqlFixture : IAsyncLifetime
{
    public const string Image = "mysql:8.4";

    /// <summary>
    /// The provider takes the migration lock <c>__{database}_EFMigrationsLock</c> with <c>GET_LOCK</c>, and MySQL allows at
    /// most 64 characters in a lock name. So a database that the migrations change has at most 45 characters in its name.
    /// </summary>
    public const int MaxDatabaseNameLength = 45;

    /// <summary>The maximum count of connections of each pool of a test database.</summary>
    public const int MaxPoolSize = 10;

    private const string _testDatabases = "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA WHERE LEFT(SCHEMA_NAME, CHAR_LENGTH(@prefix)) = @prefix";

    private readonly string _prefix = PrefixOf(Assembly.GetEntryAssembly()!.GetName().Name!);

    private readonly ConcurrentDictionary<string, MySqlTestDatabase> _databases = new(StringComparer.Ordinal);

    private MySqlContainer? _container;

    private string _server = string.Empty;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (MySqlIntegration.IsConfigured)
        {
            _server = MySqlIntegration.Settings.ConnectionString!;
        }
        else
        {
            _container = new MySqlBuilder(Image).WithUsername("root").Build();
            await _container.StartAsync(Ct);
            _server = _container.GetConnectionString();
        }

        var (version, dropped) = await DropTestDatabasesAsync(Ct);
        TestContext.Current.SendDiagnosticMessage(
            $"MySQL {version} from {(_container is null ? "MySqlIntegration:ConnectionString" : Image)}: dropped {dropped} test databases with the prefix {_prefix} at the start.");
    }

    public async ValueTask DisposeAsync()
    {
        MySqlConnection.ClearAllPools();
        var (_, dropped) = await DropTestDatabasesAsync(CancellationToken.None);
        TestContext.Current.SendDiagnosticMessage($"Dropped {dropped} test databases with the prefix {_prefix} at the end.");
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// The database of one name, the same instance for each call with that name, with the table prefix of its tables and the
    /// create step of its first call.
    /// </summary>
    /// <summary>
    /// The prefix of the PostgreSQL rule of D180 with no <c>_data_efcore</c>, so that the 45 characters of
    /// <see cref="MaxDatabaseNameLength"/> keep 15 characters for a name of the subscription project:
    /// <c>msghatch_subscriptions_mysql__</c> has 30.
    /// </summary>
    public static string PrefixOf(string testProject) =>
        PostgreSqlFixture.PrefixOf(testProject).Replace("_data_efcore", string.Empty, StringComparison.Ordinal);

    public MySqlTestDatabase Database(string name, string tablePrefix = "", Func<MySqlTestDatabase, CancellationToken, Task>? create = null) =>
        _databases.GetOrAdd(
            name,
            static (name, state) => new MySqlTestDatabase(state.Fixture.ConnectionStringOf(state.Fixture.DatabaseNameOf(name)), state.TablePrefix, state.Create),
            (Fixture: this, TablePrefix: tablePrefix, Create: create));

    private string DatabaseNameOf(string name)
    {
        var database = $"{_prefix}{name}";
        return database.Length <= MaxDatabaseNameLength
            ? database
            : throw new ArgumentException($"The database name {database} has more than {MaxDatabaseNameLength} characters.", nameof(name));
    }

    /// <summary>
    /// The connection string of a database, with a pool of at most <see cref="MaxPoolSize"/> connections, because MySQL
    /// allows 151 connections by default, and each test database has its own pools.
    /// </summary>
    private string ConnectionStringOf(string database) =>
        new MySqlConnectionStringBuilder(_server) { Database = database, MaximumPoolSize = MaxPoolSize }.ConnectionString;

    /// <summary>Drops each database whose name starts with the prefix of the test project, and gives the version of the server.</summary>
    private async Task<(string Version, int Dropped)> DropTestDatabasesAsync(CancellationToken ct)
    {
        await using var connection = new MySqlConnection(new MySqlConnectionStringBuilder(_server) { Database = string.Empty }.ConnectionString);
        await connection.OpenAsync(ct);
        var names = new List<string>();
        await using (var select = new MySqlCommand(_testDatabases, connection))
        {
            select.Parameters.AddWithValue("@prefix", _prefix);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        foreach (var name in names)
        {
            await using var drop = new MySqlCommand($"DROP DATABASE {MySqlTestDatabase.Quote(name)}", connection);
            await drop.ExecuteNonQueryAsync(ct);
        }

        return (connection.ServerVersion, names.Count);
    }
}
