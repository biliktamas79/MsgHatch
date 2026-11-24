using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Data.SqlClient;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// One SQL Server for the test assembly (D180): the server of <see cref="SqlServerIntegration"/>, for example the local
/// container dev-mssql, or else SQL Server in a container through Testcontainers (D124). Each test class uses its own
/// database. Each database name starts with the name of the test project, so the fixture touches no other database: it
/// drops these databases at the start of a run, for example after a crashed run, and at the end of the run. A database
/// is created again at its first use. A machine with no image of the Ryuk container of Testcontainers sets
/// <c>TESTCONTAINERS_RYUK_DISABLED=true</c> for its run (D177).
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2025-CU7-ubuntu-24.04";

    private const string _testDatabases = """
        SELECT name FROM sys.databases WHERE LEFT(name, LEN(@prefix)) = @prefix COLLATE Latin1_General_100_BIN2
        """;

    private const string _drop = """
        DECLARE @sql nvarchar(max) =
            N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name);
        EXEC (@sql);
        """;

    private readonly string _prefix = $"{Assembly.GetEntryAssembly()!.GetName().Name!.Replace('.', '_')}_";

    private readonly ConcurrentDictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    private MsSqlContainer? _container;

    private string _server = string.Empty;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (SqlServerIntegration.IsConfigured)
        {
            _server = SqlServerIntegration.Settings.ConnectionString!;
        }
        else
        {
            _container = new MsSqlBuilder(Image).Build();
            await _container.StartAsync(Ct);
            _server = _container.GetConnectionString();
        }

        var (serverName, dropped) = await DropTestDatabasesAsync(Ct);
        TestContext.Current.SendDiagnosticMessage(
            $"SQL Server {serverName} from {(_container is null ? "SqlServerIntegration:ConnectionString" : Image)}: dropped {dropped} test databases with the prefix {_prefix} at the start.");
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        var (_, dropped) = await DropTestDatabasesAsync(CancellationToken.None);
        TestContext.Current.SendDiagnosticMessage($"Dropped {dropped} test databases with the prefix {_prefix} at the end.");
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>The database of one name, the same instance for each call with that name, with the create step of its first call.</summary>
    public TestDatabase Database(string name, Func<TestDatabase, CancellationToken, Task>? create = null) =>
        _databases.GetOrAdd(
            name, static (name, state) => new TestDatabase(state.Fixture.ConnectionStringOf($"{state.Fixture._prefix}{name}"), state.Create), (Fixture: this, Create: create));

    private string ConnectionStringOf(string database) => new SqlConnectionStringBuilder(_server) { InitialCatalog = database }.ConnectionString;

    /// <summary>Drops each database whose name starts with the prefix of the test project, and gives the name of the server.</summary>
    private async Task<(string ServerName, int Dropped)> DropTestDatabasesAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionStringOf("master"));
        await connection.OpenAsync(ct);
        var names = new List<string>();
        await using (var select = new SqlCommand(_testDatabases, connection))
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
            await using var drop = new SqlCommand(_drop, connection);
            drop.Parameters.AddWithValue("@name", name);
            await drop.ExecuteNonQueryAsync(ct);
        }

        await using var serverName = new SqlCommand("SELECT @@SERVERNAME", connection);
        return ((string)(await serverName.ExecuteScalarAsync(ct))!, names.Count);
    }
}
