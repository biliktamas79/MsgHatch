using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.Sqlite;
using MsgHatch.Data.Sqlite.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;

namespace MsgHatch.Subscriptions.Data.Sqlite.DependencyInjection;

/// <summary>The setup method of the SQLite subscription store (section 4.7, D60).</summary>
public static class SqliteSubscriptionStoreExtensions
{
    /// <summary>
    /// Selects <see cref="SqliteSubscriptionStore"/>, with its logging wrapper, one instance for each tenant key. The store
    /// creates its tables one time for each connection string. It shares the <see cref="SqliteStoreOptions"/> of
    /// <c>UseSqliteStores</c> (D59): the options bind from the section <c>MsgHatch:Sqlite</c>, and a value from code wins.
    /// A value from code here and in <c>UseSqliteStores</c> fails the startup validation (D150).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSqliteSubscriptionStore(this MsgHatchBuilder builder, Action<SqliteStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        SqliteStoreExtensions.AddOptions(builder, nameof(UseSqliteSubscriptionStore), configure);
        return builder.UseSubscriptionStore(
            static (services, key) => new SqliteSubscriptionStore(
                SqliteStoreExtensions.ConnectionStringOf(services), key, services.GetRequiredService<ChannelSettingsJson>()),
            nameof(UseSqliteSubscriptionStore));
    }

    /// <summary>Selects the SQLite subscription store on a database.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="connectionString">The connection string of the database, for example <c>Data Source=msghatch.db</c>.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSqliteSubscriptionStore(this MsgHatchBuilder builder, string connectionString) =>
        builder.UseSqliteSubscriptionStore(o => o.ConnectionString = connectionString);
}
