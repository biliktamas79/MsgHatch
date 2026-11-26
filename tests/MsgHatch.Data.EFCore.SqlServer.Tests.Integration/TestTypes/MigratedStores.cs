using MsgHatch.Tests.Shared;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Integration.TestTypes;

/// <summary>
/// The stores of <c>MsgHatch.Data.EFCore.SqlServer</c> with its dialect, on a database of the fixture that the migrations of
/// the package created (D320). The services of a database live until the end of the run.
/// </summary>
public static class MigratedStores
{
    private static readonly ConcurrentDictionary<string, ServiceProvider> _services = new(StringComparer.Ordinal);

    /// <summary>The option <c>JsonColumnType = "json"</c> of the contract classes of D323.</summary>
    public static Action<EntityFrameworkStoreOptions> WithJson { get; } = static o => o.JsonColumnType = "json";

    /// <summary>
    /// The database of one name, which the migrations of the package create at its first reset, with the options of its
    /// first call.
    /// </summary>
    public static TestDatabase Of(SqlServerFixture server, string name, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        server.Database(name, (database, ct) => ServicesOf(database, configureOptions).MigrateMsgHatchAsync(ct: ct));

    /// <summary>
    /// The database <c>host</c> of the inbox in the transaction of the host (D331): the migrations of the package create the
    /// MsgHatch tables, and the context of the host creates only its own table <c>Orders</c>.
    /// </summary>
    public static TestDatabase OfHost(SqlServerFixture server) =>
        server.Database("host", static async (database, ct) =>
        {
            await ServicesOf(database).MigrateMsgHatchAsync(ct: ct);
            await using var host = HostContextOf(database);
            await host.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);
        });

    /// <summary>
    /// New services of <c>UseEntityFrameworkSqlServerStores&lt;TestHostContext&gt;</c> on the database, with the role Consumer, the
    /// context of the host on the same database, and a dispatch signal that replaces the registration of the setup.
    /// </summary>
    internal static ServiceProvider BuildWithHostContext(TestDatabase database, IDispatchSignal signal)
    {
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddDbContext<TestHostContext>(o => o.UseSqlServer(database.ConnectionString));
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseEntityFrameworkSqlServerStores<TestHostContext>(database.ConnectionString)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m.WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>()));
        services.Replace(ServiceDescriptor.Singleton(signal));
        return services.Build();
    }

    /// <summary>A new context of the host on the database, with the dialect of the package, as the generic setup method gives it.</summary>
    internal static TestHostContext HostContextOf(TestDatabase database)
    {
        var options = new DbContextOptionsBuilder<TestHostContext>().UseSqlServer(database.ConnectionString);
        options.IgnoreManyServiceProvidersWarning();
        options.UseEntityFrameworkStoreDialect(SqlServerStoreDialect.Instance);
        return new TestHostContext(options.Options);
    }

    /// <summary>The pooled factory of the contexts of the stores of the database.</summary>
    public static IDbContextFactory<MsgHatchDbContext> ContextsOf(TestDatabase database, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        ServicesOf(database, configureOptions).GetRequiredService<IDbContextFactory<MsgHatchDbContext>>();

    /// <summary>The services of <c>UseEntityFrameworkSqlServerStores</c> on the database, one instance for each database.</summary>
    public static ServiceProvider ServicesOf(TestDatabase database, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        _services.GetOrAdd(database.ConnectionString, static (connectionString, configure) => Build(connectionString, configure), configureOptions);

    /// <summary>New services of <c>UseEntityFrameworkSqlServerStores</c> with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkSqlServerStores(connectionString, configureOptions);
        return services.Build();
    }
}
