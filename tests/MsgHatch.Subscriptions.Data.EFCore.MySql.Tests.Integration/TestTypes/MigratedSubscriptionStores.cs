using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration.TestTypes;

/// <summary>
/// The subscription store of <c>MsgHatch.Subscriptions.Data.EFCore.MySql</c> with the dialect, on a database of the
/// fixture that the migrations of both MySQL packages created (D320). The tables have the prefix <c>MsgHatch</c> (D326).
/// The services of a database live until the end of the run.
/// </summary>
public static class MigratedSubscriptionStores
{
    public const string TablePrefix = "MsgHatch";

    private static readonly ConcurrentDictionary<string, ServiceProvider> _services = new(StringComparer.Ordinal);

    /// <summary>
    /// The database of one name, which the migrations of both packages create at its first reset, with the options of its
    /// first call.
    /// </summary>
    public static MySqlTestDatabase Of(MySqlFixture server, string name, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        server.Database(name, TablePrefix, (database, ct) => ServicesOf(database, configureOptions).MigrateMsgHatchAsync(ct: ct));

    /// <summary>The store of a tenant key on the database.</summary>
    public static EntityFrameworkSubscriptionStore StoreOf(MySqlTestDatabase database, TenantKey key) =>
        new(ServicesOf(database).GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>(), key, ScenarioContentTypes.SettingsJson);

    /// <summary>The services of both MySQL setup methods on the database, one instance for each database.</summary>
    public static ServiceProvider ServicesOf(MySqlTestDatabase database, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        _services.GetOrAdd(database.ConnectionString, static (connectionString, configure) => Build(connectionString, configure), configureOptions);

    /// <summary>New services of both MySQL setup methods with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseEntityFrameworkMySqlStores(connectionString, configureOptions)
            .UseEntityFrameworkMySqlSubscriptionStore(connectionString);
        return services.Build();
    }

    /// <summary>The test data of a migrated database: a reset empties the tables of the stores and of the subscriptions (<see cref="MySqlTestDatabase.ResetAsync"/>).</summary>
    public sealed class TestData(MySqlTestDatabase database) : ISubscriptionStoreTestData
    {
        public async ValueTask ResetAsync(CancellationToken ct)
        {
            await database.ResetAsync(ct);
            await database.ExecuteAsync($"TRUNCATE TABLE {database.Table("Subscription")}", ct);
        }
    }
}
