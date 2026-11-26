using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;

namespace MsgHatch.Subscriptions.Caching.Hybrid.DependencyInjection;

/// <summary>The setup method of the HybridCache subscription cache backend (section 4.7, D60).</summary>
public static class HybridSubscriptionCacheExtensions
{
    /// <summary>
    /// Turns on the subscription cache on HybridCache: <see cref="HybridCacheSubscriptionBackend"/>, the caching wrapper,
    /// <see cref="ISubscriptionCache"/>, and HybridCache with <c>AddHybridCache</c>. A HybridCache that the host already
    /// registered stays. The options bind from the section <c>SubscriptionCache</c> under the root of
    /// <c>AddMsgHatch</c>, by default <c>MsgHatch:SubscriptionCache</c>, and a value from code wins.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the subscription cache options in code.</param>
    /// <param name="configureHybridCache">An optional change of the options of HybridCache, which the whole host shares.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseHybridSubscriptionCache(
        this MsgHatchBuilder builder, Action<SubscriptionCacheOptions>? configure = null, Action<HybridCacheOptions>? configureHybridCache = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = configureHybridCache is null ? builder.Services.AddHybridCache() : builder.Services.AddHybridCache(configureHybridCache);
        return builder.UseSubscriptionCache(
            static services => new HybridCacheSubscriptionBackend(services.GetRequiredService<HybridCache>()),
            configure,
            nameof(UseHybridSubscriptionCache));
    }
}
