using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Integration.TestTypes;

/// <summary>
/// The stores of <c>MsgHatch.Data.EFCore.SqlServer</c> with its dialect, on a database of the fixture that the migrations of
/// the package created (D320). The services of a database live until the end of the run.
/// </summary>
public static partial class MigratedStores
{
    /// <summary>The query of the columns of a database, in a stable order.</summary>
    public const string ColumnsQuery = """
        SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLLATION_NAME
        FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME
        """;

    /// <summary>The query of the indexes of a database with their columns, in a stable order.</summary>
    public const string IndexesQuery = """
        SELECT SCHEMA_NAME(t.schema_id), t.name, i.name, i.type_desc, i.is_unique,
            STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal), i.filter_definition
        FROM sys.indexes AS i JOIN sys.tables AS t ON t.object_id = i.object_id
        JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        GROUP BY t.schema_id, t.name, i.name, i.type_desc, i.is_unique, i.filter_definition
        ORDER BY 1, 2, 3
        """;

    private static readonly ConcurrentDictionary<string, ServiceProvider> _services = new(StringComparer.Ordinal);

    /// <summary>The database of one name, which the migrations of the package create at its first reset.</summary>
    public static TestDatabase Of(SqlServerFixture server, string name) =>
        server.Database(name, static (database, ct) => ServicesOf(database).MigrateMsgHatchAsync(ct: ct));

    /// <summary>The pooled factory of the contexts of the stores of the database.</summary>
    public static IDbContextFactory<MsgHatchDbContext> ContextsOf(TestDatabase database) =>
        ServicesOf(database).GetRequiredService<IDbContextFactory<MsgHatchDbContext>>();

    /// <summary>The services of <c>UseEntityFrameworkSqlServerStores</c> on the database, one instance for each database.</summary>
    public static ServiceProvider ServicesOf(TestDatabase database) =>
        _services.GetOrAdd(database.ConnectionString, static connectionString => Build(connectionString));

    /// <summary>New services of <c>UseEntityFrameworkSqlServerStores</c> with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkSqlServerStores(connectionString, configureOptions);
        return services.Build();
    }

    /// <summary>Runs a SQL script batch by batch, with the batch separator <c>GO</c> of the scripts of EF Core.</summary>
    public static async Task RunScriptAsync(TestDatabase database, string script, CancellationToken ct)
    {
        foreach (var batch in BatchSeparator().Split(script).Where(static batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await database.ExecuteAsync(batch, ct);
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();
}
