using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

[assembly: AssemblyFixture(typeof(PostgreSqlFixture))]

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.PostgreSql.TestTypes;

/// <summary>
/// The test data of the EF Core subscription store on PostgreSQL with no dialect (D318): one database of the PostgreSQL of
/// the fixture, with the default schema <c>msghatch</c> and the collation of the database. The first reset creates it with
/// <c>EnsureCreatedAsync</c>, and each reset deletes the rows.
/// </summary>
public sealed class PostgreSqlSubscriptionStoreTestData : ISubscriptionStoreTestData
{
    private static readonly ConcurrentDictionary<string, PostgreSqlSubscriptionStoreTestData> _databases = new(StringComparer.Ordinal);

    private bool _created;

    private PostgreSqlSubscriptionStoreTestData(PostgreSqlTestDatabase raw)
    {
        Raw = raw;
        Contexts = PostgreSqlTestDatabase.ContextsOf<MsgHatchSubscriptionDbContext>(
            builder => builder.UseEntityFrameworkSubscriptionStore(o => o.UseNpgsql(raw.ConnectionString)));
    }

    /// <summary>The raw reads and writes of the database.</summary>
    public PostgreSqlTestDatabase Raw { get; }

    public IDbContextFactory<MsgHatchSubscriptionDbContext> Contexts { get; }

    /// <summary>The test data of the database of one name, the same instance for each call with that name.</summary>
    public static PostgreSqlSubscriptionStoreTestData Of(PostgreSqlFixture server, string name) =>
        _databases.GetOrAdd(name, static (name, server) => new PostgreSqlSubscriptionStoreTestData(server.Database(name)), server);

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

        await Raw.ExecuteAsync("""DELETE FROM "msghatch"."MsgHatchSubscription" """, ct);
    }
}
