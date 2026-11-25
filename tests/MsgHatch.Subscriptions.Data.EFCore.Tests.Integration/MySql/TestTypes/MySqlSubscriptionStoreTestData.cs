using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

[assembly: AssemblyFixture(typeof(MySqlFixture))]

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.MySql.TestTypes;

/// <summary>
/// The test data of the EF Core subscription store on MySQL with no dialect (D318): one database of the MySQL of the
/// fixture, where the provider ignores the schema of the model, with the binary collation of the option. The first reset
/// creates the table with <c>EnsureCreatedAsync</c>, and each reset empties it with <c>TRUNCATE TABLE</c> (<see cref="MySqlTestDatabase.ResetAsync"/>).
/// </summary>
public sealed class MySqlSubscriptionStoreTestData : ISubscriptionStoreTestData
{
    private static readonly ConcurrentDictionary<string, MySqlSubscriptionStoreTestData> _databases = new(StringComparer.Ordinal);

    private bool _created;

    private MySqlSubscriptionStoreTestData(MySqlTestDatabase raw)
    {
        Raw = raw;
        Contexts = MySqlTestDatabase.ContextsOf<MsgHatchSubscriptionDbContext>(
            builder => builder.UseEntityFrameworkSubscriptionStore(o => MySqlTestDatabase.UseMySqlWithNoDialect(o, raw.ConnectionString)));
    }

    /// <summary>The raw reads and writes of the database.</summary>
    public MySqlTestDatabase Raw { get; }

    public IDbContextFactory<MsgHatchSubscriptionDbContext> Contexts { get; }

    /// <summary>The test data of the database of one name, the same instance for each call with that name.</summary>
    public static MySqlSubscriptionStoreTestData Of(MySqlFixture server, string name) =>
        _databases.GetOrAdd(name, static (name, server) => new MySqlSubscriptionStoreTestData(server.Database(name)), server);

    /// <summary>Gives the store of a tenant key on the database.</summary>
    public EntityFrameworkSubscriptionStore StoreOf(TenantKey key) => new(Contexts, key, ScenarioContentTypes.SettingsJson);

    public async ValueTask ResetAsync(CancellationToken ct)
    {
        if (!_created)
        {
            await using var context = await Contexts.CreateDbContextAsync(ct);
            await context.Database.EnsureCreatedAsync(ct);
            _created = true;
        }

        await Raw.ExecuteAsync($"TRUNCATE TABLE {Raw.Table("Subscription")}", ct);
    }
}
