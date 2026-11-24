using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the EF Core subscription store on SQLite: one database file with a pooled factory of the contexts. A
/// reset makes a new file with the tables.
/// </summary>
public sealed class EntityFrameworkSubscriptionStoreTestData : ISubscriptionStoreTestData
{
    private static readonly ConcurrentDictionary<EntityFrameworkSubscriptionStore, EntityFrameworkSubscriptionStoreTestData> _ofStore = new();

    private EntityFrameworkSubscriptionStoreTestData(SqliteTestDatabase database)
    {
        Database = database;
        Contexts = database.ContextsOf<MsgHatchSubscriptionDbContext>();
    }

    public SqliteTestDatabase Database { get; }

    public IDbContextFactory<MsgHatchSubscriptionDbContext> Contexts { get; }

    /// <summary>A new database file, and the store of each tenant key on it.</summary>
    public static Func<TenantKey, EntityFrameworkSubscriptionStore> StoresOnANewFile()
    {
        var data = new EntityFrameworkSubscriptionStoreTestData(new SqliteTestDatabase());
        return key =>
        {
            var store = new EntityFrameworkSubscriptionStore(data.Contexts, key, ScenarioContentTypes.SettingsJson);
            _ofStore[store] = data;
            return store;
        };
    }

    /// <summary>The test data of the file of a store that <see cref="StoresOnANewFile"/> made.</summary>
    public static EntityFrameworkSubscriptionStoreTestData Of(EntityFrameworkSubscriptionStore store) => _ofStore[store];

    public ValueTask ResetAsync(CancellationToken ct) => Database.ResetAsync(Contexts, ct);
}
