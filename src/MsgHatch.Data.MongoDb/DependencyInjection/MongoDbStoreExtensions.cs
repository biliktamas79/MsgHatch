using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.MongoDb.DependencyInjection;

/// <summary>The setup method of the MongoDB stores (section 4.7, D60).</summary>
public static class MongoDbStoreExtensions
{
    /// <summary>
    /// Selects the MongoDB stores (D121): <see cref="MongoDbInboxStore"/> and <see cref="MongoDbOutboxStore"/>, each with
    /// its two parts and their logging wrappers, and <see cref="MongoDbRegistrySettingsStore"/> with its logging wrapper,
    /// on the database of <see cref="MongoDbStoreOptions.DatabaseName"/>. It registers an <see cref="IMongoClient"/> for
    /// <see cref="MongoDbStoreOptions.ConnectionString"/> when the host has not registered one. The stores create their
    /// indexes one time for each database in the process. The options bind from the section <c>MongoDb</c> under the
    /// root of <c>AddMsgHatch</c>, by default <c>MsgHatch:MongoDb</c>, and a value from code wins.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseMongoDbStores(this MsgHatchBuilder builder, Action<MongoDbStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddOptionsAndClient(builder, nameof(UseMongoDbStores), configure);
        builder.Services.TryAddSingleton(static services => new MongoDbInboxStore(DatabaseOf(services), services.GetRequiredService<MsgHatchJson>()));
        builder.Services.TryAddSingleton(static services => new MongoDbOutboxStore(DatabaseOf(services), services.GetRequiredService<MsgHatchJson>()));
        builder.Services.TryAddSingleton(static services => new MongoDbRegistrySettingsStore(DatabaseOf(services)));
        return builder
            .UseRegistrySettingsStore(static services => services.GetRequiredService<MongoDbRegistrySettingsStore>(), nameof(UseMongoDbStores))
            .UseInboxStore(static (services, key) => services.GetRequiredService<MongoDbInboxStore>().For(key), nameof(UseMongoDbStores))
            .UseInboxDispatchStore(static services => services.GetRequiredService<MongoDbInboxStore>(), nameof(UseMongoDbStores))
            .UseOutboxStore(static (services, key) => services.GetRequiredService<MongoDbOutboxStore>().For(key), nameof(UseMongoDbStores))
            .UseOutboxDispatchStore(static services => services.GetRequiredService<MongoDbOutboxStore>(), nameof(UseMongoDbStores));
    }

    /// <summary>
    /// Registers the shared <see cref="MongoDbStoreOptions"/> of a setup method of the MongoDB stores (D59): the binding of
    /// the section <c>MongoDb</c> under the root of <c>AddMsgHatch</c>, by default <c>MsgHatch:MongoDb</c>, the
    /// validation, and a value in code through <c>ConfigureSharedOptions</c> (D150). It also registers an
    /// <see cref="IMongoClient"/> for the connection string with <c>TryAdd</c>, so a client of the host stays (D60).
    /// </summary>
    internal static void AddOptionsAndClient(MsgHatchBuilder builder, string setupMethod, Action<MongoDbStoreOptions>? configure)
    {
        builder.Services.AddOptions<MongoDbStoreOptions>().BindConfiguration(builder.SectionPathOf(MongoDbStoreOptions.SectionKey)).ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MongoDbStoreOptions>, MongoDbStoreOptionsValidator>());
        if (configure is not null)
        {
            builder.ConfigureSharedOptions(setupMethod, configure);
        }

        builder.Services.TryAddSingleton<IMongoClient>(
            static services => new MongoClient(services.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value.ConnectionString));
    }

    /// <summary>The database of the stores, from the shared client and options.</summary>
    internal static IMongoDatabase DatabaseOf(IServiceProvider services) =>
        services.GetRequiredService<IMongoClient>().GetDatabase(services.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value.DatabaseName);
}
