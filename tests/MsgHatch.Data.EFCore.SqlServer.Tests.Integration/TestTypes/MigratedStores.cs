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
}
