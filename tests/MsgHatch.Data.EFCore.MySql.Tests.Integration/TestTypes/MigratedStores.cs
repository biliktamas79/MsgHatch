using MsgHatch.Tests.Shared;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.MySql.Tests.Integration.TestTypes;

/// <summary>
/// The stores of <c>MsgHatch.Data.EFCore.MySql</c> with its dialect, on a database of the fixture that the migrations of
/// the package created (D320). The tables have the prefix <c>MsgHatch</c> (D326). The services of a database live until the
/// end of the run.
/// </summary>
public static class MigratedStores
{
    public const string TablePrefix = "MsgHatch";

    private static readonly ConcurrentDictionary<string, ServiceProvider> _services = new(StringComparer.Ordinal);

    /// <summary>
    /// The database of one name, which the migrations of the package create at its first reset, with the options of its
    /// first call.
    /// </summary>
    public static MySqlTestDatabase Of(MySqlFixture server, string name, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        server.Database(name, TablePrefix, (database, ct) => ServicesOf(database, configureOptions).MigrateMsgHatchAsync(ct: ct));

    /// <summary>
    /// The database <c>host</c> of the inbox in the transaction of the host (D331): the migrations of the package create the
    /// MsgHatch tables, and the context of the host creates only its own table <c>Orders</c>.
    /// </summary>
    public static MySqlTestDatabase OfHost(MySqlFixture server) =>
        server.Database("host", TablePrefix, static async (database, ct) =>
        {
            await ServicesOf(database).MigrateMsgHatchAsync(ct: ct);
            await using var host = HostContextOf(database);
            await host.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);
        });

    /// <summary>A new context of the host on the database, with the dialect of the package, as the generic setup method gives it.</summary>
    internal static TestHostContext HostContextOf(MySqlTestDatabase database)
    {
        var options = new DbContextOptionsBuilder<TestHostContext>().UseMySql(database.ConnectionString, MySqlTestDatabase.ServerVersion);
        options.IgnoreManyServiceProvidersWarning();
        options.UseEntityFrameworkStoreDialect(MySqlStoreDialect.Instance);
        return new TestHostContext(options.Options);
    }

    /// <summary>The pooled factory of the contexts of the stores of the database.</summary>
    public static IDbContextFactory<MsgHatchDbContext> ContextsOf(MySqlTestDatabase database, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        ServicesOf(database, configureOptions).GetRequiredService<IDbContextFactory<MsgHatchDbContext>>();

    /// <summary>The services of <c>UseEntityFrameworkMySqlStores</c> on the database, one instance for each database.</summary>
    public static ServiceProvider ServicesOf(MySqlTestDatabase database, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        _services.GetOrAdd(database.ConnectionString, static (connectionString, configure) => Build(connectionString, configure), configureOptions);

    /// <summary>New services of <c>UseEntityFrameworkMySqlStores</c> with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkMySqlStores(connectionString, configureOptions);
        return services.Build();
    }
}
