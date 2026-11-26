using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.Sqlite.DependencyInjection;

/// <summary>The setup method of the SQLite stores (section 4.7, D60).</summary>
public static class SqliteStoreExtensions
{
    /// <summary>
    /// Selects the SQLite stores (D55): <see cref="SqliteInboxStore"/> and <see cref="SqliteOutboxStore"/> on one
    /// database, each with its two parts and their logging wrappers, and <see cref="SqliteRegistrySettingsStore"/> on the
    /// same database with its logging wrapper. The stores create their tables one time for each connection string. The
    /// options bind from the section <c>Sqlite</c> under the root of <c>AddMsgHatch</c>, by default
    /// <c>MsgHatch:Sqlite</c>, and a value from code wins.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSqliteStores(this MsgHatchBuilder builder, Action<SqliteStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddOptions(builder, nameof(UseSqliteStores), configure);
        builder.Services.TryAddSingleton(static services => new SqliteInboxStore(ConnectionStringOf(services), services.GetRequiredService<MsgHatchJson>()));
        builder.Services.TryAddSingleton(static services => new SqliteOutboxStore(ConnectionStringOf(services), services.GetRequiredService<MsgHatchJson>()));
        builder.Services.TryAddSingleton(static services => new SqliteRegistrySettingsStore(ConnectionStringOf(services)));
        return builder
            .UseRegistrySettingsStore(static services => services.GetRequiredService<SqliteRegistrySettingsStore>(), nameof(UseSqliteStores))
            .UseInboxStore(static (services, key) => services.GetRequiredService<SqliteInboxStore>().For(key), nameof(UseSqliteStores))
            .UseInboxDispatchStore(static services => services.GetRequiredService<SqliteInboxStore>(), nameof(UseSqliteStores))
            .UseOutboxStore(static (services, key) => services.GetRequiredService<SqliteOutboxStore>().For(key), nameof(UseSqliteStores))
            .UseOutboxDispatchStore(static services => services.GetRequiredService<SqliteOutboxStore>(), nameof(UseSqliteStores));
    }

    /// <summary>Selects the SQLite stores on a database.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="connectionString">The connection string of the database, for example <c>Data Source=msghatch.db</c>.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSqliteStores(this MsgHatchBuilder builder, string connectionString) =>
        builder.UseSqliteStores(o => o.ConnectionString = connectionString);

    /// <summary>
    /// Selects the SQLite stores with one database file for each tenant (D125): one <see cref="SqliteInboxStore"/> and
    /// one <see cref="SqliteOutboxStore"/> for each key, as keyed singletons on the file of that key, as the tenant stores,
    /// and the dispatch composites of the core through <c>UseStorageForEachTenant</c>. The key
    /// <see cref="TenantKey.None"/> and <see cref="SqliteRegistrySettingsStore"/> use <c>SystemConnectionString</c>. The
    /// stores create their tables one time for each connection string. The options bind from the section
    /// <c>SqliteTenants</c> under the root of <c>AddMsgHatch</c>, by default <c>MsgHatch:SqliteTenants</c>, and a value
    /// from code wins.
    /// </summary>
    /// <typeparam name="TTenantSource">The tenant source of the consumer, which lists the tenants with their own file.</typeparam>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSqliteTenantStores<TTenantSource>(
        this MsgHatchBuilder builder, Action<SqliteTenantStoreOptions>? configure = null)
        where TTenantSource : class, ITenantSource
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddTenantOptions(builder, nameof(UseSqliteTenantStores), configure);
        builder.Services.TryAddKeyedSingleton(
            KeyedService.AnyKey,
            static (services, key) => new SqliteInboxStore(ConnectionStringOf(services, (TenantKey)key!), services.GetRequiredService<MsgHatchJson>()));
        builder.Services.TryAddKeyedSingleton(
            KeyedService.AnyKey,
            static (services, key) => new SqliteOutboxStore(ConnectionStringOf(services, (TenantKey)key!), services.GetRequiredService<MsgHatchJson>()));
        builder.Services.TryAddSingleton(static services => new SqliteRegistrySettingsStore(ConnectionStringOf(services, TenantKey.None)));
        return builder
            .UseRegistrySettingsStore(static services => services.GetRequiredService<SqliteRegistrySettingsStore>(), nameof(UseSqliteTenantStores))
            .UseStorageForEachTenant<TTenantSource>(
                static (services, key) => services.GetRequiredKeyedService<SqliteInboxStore>(key).For(key),
                static (services, key) => services.GetRequiredKeyedService<SqliteInboxStore>(key),
                static (services, key) => services.GetRequiredKeyedService<SqliteOutboxStore>(key).For(key),
                static (services, key) => services.GetRequiredKeyedService<SqliteOutboxStore>(key),
                nameof(UseSqliteTenantStores));
    }

    /// <summary>
    /// Registers the shared <see cref="SqliteTenantStoreOptions"/> of a setup method with one database file for each
    /// tenant (D59): the binding of the section <c>SqliteTenants</c> under the root of <c>AddMsgHatch</c>, by default
    /// <c>MsgHatch:SqliteTenants</c>, the validation, and a value in code through
    /// <c>ConfigureSharedOptions</c> (D150).
    /// </summary>
    internal static void AddTenantOptions(MsgHatchBuilder builder, string setupMethod, Action<SqliteTenantStoreOptions>? configure)
    {
        builder.Services.AddOptions<SqliteTenantStoreOptions>().BindConfiguration(builder.SectionPathOf(SqliteTenantStoreOptions.SectionKey)).ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SqliteTenantStoreOptions>, SqliteTenantStoreOptionsValidator>());
        if (configure is not null)
        {
            builder.ConfigureSharedOptions(setupMethod, configure);
        }
    }

    /// <summary>The connection string of the database of a key, from <see cref="SqliteTenantStoreOptions"/>.</summary>
    internal static string ConnectionStringOf(IServiceProvider services, TenantKey key) =>
        services.GetRequiredService<IOptions<SqliteTenantStoreOptions>>().Value.ConnectionStringOf(key);

    /// <summary>
    /// Registers the shared <see cref="SqliteStoreOptions"/> of a setup method of the SQLite stores (D59): the binding of
    /// the section <c>Sqlite</c> under the root of <c>AddMsgHatch</c>, by default <c>MsgHatch:Sqlite</c>, the
    /// validation, and a value in code through <c>ConfigureSharedOptions</c> (D150).
    /// </summary>
    internal static void AddOptions(MsgHatchBuilder builder, string setupMethod, Action<SqliteStoreOptions>? configure)
    {
        builder.Services.AddOptions<SqliteStoreOptions>().BindConfiguration(builder.SectionPathOf(SqliteStoreOptions.SectionKey)).ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SqliteStoreOptions>, SqliteStoreOptionsValidator>());
        if (configure is not null)
        {
            builder.ConfigureSharedOptions(setupMethod, configure);
        }
    }

    internal static string ConnectionStringOf(IServiceProvider services) =>
        services.GetRequiredService<IOptions<SqliteStoreOptions>>().Value.ConnectionString;
}
