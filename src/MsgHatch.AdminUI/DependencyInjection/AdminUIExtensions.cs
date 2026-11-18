using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MsgHatch.AdminUI.DependencyInjection;

/// <summary>The setup method of the registry pages of the core admin UI (D272, D273).</summary>
public static class AdminUIExtensions
{
    /// <summary>
    /// Adds the registry pages of the core admin UI. The pages use <c>IRegistryQueries</c> of <c>TenantKey.None</c>, so the
    /// host selects the HTTP client or the in-process implementation in its setup (D235, D269). The host adds the
    /// authorization, and maps the pages with the assembly of <see cref="RegistryAdminRoutes"/> as an additional assembly of
    /// its router. The method keeps the pages in a trimmed host (D250). The channel pages read the time from
    /// <see cref="TimeProvider"/>, so the method adds <see cref="TimeProvider.System"/> when the host has none (D279).
    /// </summary>
    /// <param name="services">The services of the host.</param>
    /// <returns>The same services.</returns>
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChannelListPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChannelViewPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MessageTypeListPage))]
    public static IServiceCollection AddMsgHatchAdminUI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
