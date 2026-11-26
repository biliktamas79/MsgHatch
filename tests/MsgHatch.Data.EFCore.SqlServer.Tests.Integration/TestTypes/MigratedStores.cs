using MsgHatch.Tests.Shared;
using System.Collections.Concurrent;
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
