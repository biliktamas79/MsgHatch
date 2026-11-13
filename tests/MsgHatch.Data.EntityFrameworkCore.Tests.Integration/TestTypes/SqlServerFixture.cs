using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using MsgHatch.Data.EntityFrameworkCore.Tests.Integration.TestTypes;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace MsgHatch.Data.EntityFrameworkCore.Tests.Integration.TestTypes;

/// <summary>
/// One SQL Server in a container for the test assembly, through Testcontainers (D124). Each test class uses its own
/// database on it. The image is pinned, so a machine that has it pulls nothing. A machine with no image of the Ryuk
/// container of Testcontainers sets <c>TESTCONTAINERS_RYUK_DISABLED=true</c> for its run (D177).
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2025-CU7-ubuntu-24.04";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    private readonly ConcurrentDictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    public ValueTask InitializeAsync() => new(_container.StartAsync(TestContext.Current.CancellationToken));

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>The database of one name, the same instance for each call with that name.</summary>
    public TestDatabase Database(string name) =>
        _databases.GetOrAdd(name, static (name, container) => new TestDatabase(ConnectionStringOf(container, name)), _container);

    private static string ConnectionStringOf(MsSqlContainer container, string database) =>
        new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = $"msghatch_{database}" }.ConnectionString;
}
