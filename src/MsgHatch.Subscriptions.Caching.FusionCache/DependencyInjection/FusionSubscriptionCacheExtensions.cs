using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;

namespace MsgHatch.Subscriptions.Caching.FusionCache.DependencyInjection;

/// <summary>The setup method of the FusionCache subscription cache backend (section 4.7, D60).</summary>
public static class FusionSubscriptionCacheExtensions
{
    /// <summary>
    /// Turns on the subscription cache on FusionCache: <see cref="FusionCacheSubscriptionBackend"/>, the caching wrapper,
    /// <see cref="ISubscriptionCache"/>, and the named FusionCache <see cref="FusionCacheSubscriptionBackend.CacheName"/>.
    /// The FusionCache instances of the host do not change. The options bind from the section
    /// <c>SubscriptionCache</c> under the root of <c>AddMsgHatch</c>, by default <c>MsgHatch:SubscriptionCache</c>, and
    /// a value from code wins.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the subscription cache options in code.</param>
    /// <param name="configureFusionCache">
    /// An optional change of the named FusionCache, for example a distributed level with its serializer, or a backplane.
    /// </param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseFusionSubscriptionCache(
        this MsgHatchBuilder builder, Action<SubscriptionCacheOptions>? configure = null, Action<IFusionCacheBuilder>? configureFusionCache = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var fusionCache = builder.Services.AddFusionCache(FusionCacheSubscriptionBackend.CacheName);
        configureFusionCache?.Invoke(fusionCache);
        return builder.UseSubscriptionCache(
            static services => new FusionCacheSubscriptionBackend(services.GetRequiredService<IFusionCacheProvider>()),
            configure,
            nameof(UseFusionSubscriptionCache));
    }
}
