using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration.TestTypes;

/// <summary>
/// The subscription store of <c>MsgHatch.Subscriptions.Data.EFCore.SqlServer</c> with the dialect, on a database of the
/// fixture that the migrations of both SQL Server packages created (D320). The services of a database live until the end
/// of the run.
/// </summary>
public static class MigratedSubscriptionStores
{
    private static readonly ConcurrentDictionary<string, ServiceProvider> _services = new(StringComparer.Ordinal);

    /// <summary>The option <c>JsonColumnType = "json"</c> of the contract class of D323.</summary>
    public static Action<EntityFrameworkStoreOptions> WithJson { get; } = static o => o.JsonColumnType = "json";

    /// <summary>
    /// The database of one name, which the migrations of both packages create at its first reset, with the options of its
    /// first call.
    /// </summary>
    public static TestDatabase Of(SqlServerFixture server, string name, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        server.Database(name, (database, ct) => ServicesOf(database, configureOptions).MigrateMsgHatchAsync(ct: ct));

    /// <summary>The store of a tenant key on the database.</summary>
    public static EntityFrameworkSubscriptionStore StoreOf(TestDatabase database, TenantKey key, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        new(ServicesOf(database, configureOptions).GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>(), key, ScenarioContentTypes.SettingsJson);

    /// <summary>The services of both SQL Server setup methods on the database, one instance for each database.</summary>
    public static ServiceProvider ServicesOf(TestDatabase database, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        _services.GetOrAdd(database.ConnectionString, static (connectionString, configure) => Build(connectionString, configure), configureOptions);

    /// <summary>New services of both SQL Server setup methods with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseEntityFrameworkSqlServerStores(connectionString, configureOptions)
            .UseEntityFrameworkSqlServerSubscriptionStore(connectionString);
        return services.Build();
    }

    /// <summary>The test data of a migrated database: a reset deletes the rows of the stores and of the subscriptions.</summary>
    public sealed class TestData(TestDatabase database) : ISubscriptionStoreTestData
    {
        public async ValueTask ResetAsync(CancellationToken ct)
        {
            await database.ResetAsync(ct);
            await database.ExecuteAsync("DELETE FROM [msghatch].[Subscription]", ct);
        }
    }
}
