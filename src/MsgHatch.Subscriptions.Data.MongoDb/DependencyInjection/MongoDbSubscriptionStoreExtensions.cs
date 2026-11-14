using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.MongoDb;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;

namespace MsgHatch.Subscriptions.Data.MongoDb.DependencyInjection;

/// <summary>The setup method of the MongoDB subscription store (section 4.7, D60).</summary>
public static class MongoDbSubscriptionStoreExtensions
{
    /// <summary>
    /// Selects <see cref="MongoDbSubscriptionStore"/>, with its logging wrapper, one instance for each tenant key. It
    /// shares the <see cref="MongoDbStoreOptions"/> and the <c>IMongoClient</c> of <c>UseMongoDbStores</c> (D59): the
    /// options bind from the section <c>MsgHatch:MongoDb</c>, and a value in code follows the rule of the shared options
    /// (D150). The store creates its indexes one time for each database in the process.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the shared options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseMongoDbSubscriptionStore(this MsgHatchBuilder builder, Action<MongoDbStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        MongoDbStoreExtensions.AddOptionsAndClient(builder, nameof(UseMongoDbSubscriptionStore), configure);
        return builder.UseSubscriptionStore(
            static (services, key) => new MongoDbSubscriptionStore(
                MongoDbStoreExtensions.DatabaseOf(services), key, services.GetRequiredService<ChannelSettingsJson>()),
            nameof(UseMongoDbSubscriptionStore));
    }
}
