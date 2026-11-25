using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
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

    /// <summary>The pooled factory of the contexts of the stores of the database.</summary>
    public static IDbContextFactory<MsgHatchDbContext> ContextsOf(MySqlTestDatabase database, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        ServicesOf(database, configureOptions).GetRequiredService<IDbContextFactory<MsgHatchDbContext>>();

    /// <summary>The services of <c>UseEntityFrameworkMySqlStores</c> on the database, one instance for each database.</summary>
    public static ServiceProvider ServicesOf(MySqlTestDatabase database, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        _services.GetOrAdd(database.ConnectionString, static (connectionString, configure) => Build(connectionString, configure), configureOptions);

    /// <summary>New services of <c>UseEntityFrameworkMySqlStores</c> with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkMySqlStores(connectionString, configureOptions);
        return services.Build();
    }
}
