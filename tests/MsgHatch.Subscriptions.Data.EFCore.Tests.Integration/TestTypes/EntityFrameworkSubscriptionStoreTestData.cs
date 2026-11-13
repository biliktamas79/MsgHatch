using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// The test data of the EF Core subscription store: one database of the SQL Server of the fixture, with the default
/// schema <c>msghatch</c>. The first reset creates it with <c>EnsureCreatedAsync</c>, and each reset deletes the rows.
/// </summary>
public sealed class EntityFrameworkSubscriptionStoreTestData : ISubscriptionStoreTestData
{
    private static readonly ConcurrentDictionary<string, EntityFrameworkSubscriptionStoreTestData> _databases = new(StringComparer.Ordinal);

    private bool _created;

    private EntityFrameworkSubscriptionStoreTestData(TestDatabase raw)
    {
        Raw = raw;
        Contexts = new PooledDbContextFactory<MsgHatchSubscriptionDbContext>(
            new DbContextOptionsBuilder<MsgHatchSubscriptionDbContext>().UseSqlServer(raw.ConnectionString).Options);
    }

    /// <summary>The raw reads and writes of the database.</summary>
    public TestDatabase Raw { get; }

    public IDbContextFactory<MsgHatchSubscriptionDbContext> Contexts { get; }

    /// <summary>The test data of the database of one name, the same instance for each call with that name.</summary>
    public static EntityFrameworkSubscriptionStoreTestData Of(SqlServerFixture server, string name) =>
        _databases.GetOrAdd(name, static (name, server) => new EntityFrameworkSubscriptionStoreTestData(server.Database(name)), server);

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

        await Raw.ExecuteAsync("DELETE FROM [msghatch].[MsgHatchSubscription]", ct);
    }
}
