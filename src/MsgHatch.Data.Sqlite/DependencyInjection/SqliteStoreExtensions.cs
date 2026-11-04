using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.Sqlite.DependencyInjection;

/// <summary>The setup method of the SQLite stores (section 4.7, D60).</summary>
public static class SqliteStoreExtensions
{
    /// <summary>
    /// Selects the SQLite stores (D55): <see cref="SqliteInboxStore"/> and <see cref="SqliteOutboxStore"/> on one
    /// database, each with its two parts and their logging wrappers. The stores create their tables one time for each
    /// connection string. The options bind from the section <c>MsgHatch:Sqlite</c>, and a value from code wins.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSqliteStores(this MsgHatchBuilder builder, Action<SqliteStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<SqliteStoreOptions>().BindConfiguration(SqliteStoreOptions.SectionName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SqliteStoreOptions>, SqliteStoreOptionsValidator>());
        builder.Services.TryAddSingleton(static services => new SqliteInboxStore(ConnectionStringOf(services), services.GetRequiredService<MsgHatchJson>()));
        builder.Services.TryAddSingleton(static services => new SqliteOutboxStore(ConnectionStringOf(services), services.GetRequiredService<MsgHatchJson>()));
        return builder
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

    private static string ConnectionStringOf(IServiceProvider services) =>
        services.GetRequiredService<IOptions<SqliteStoreOptions>>().Value.ConnectionString;
}
