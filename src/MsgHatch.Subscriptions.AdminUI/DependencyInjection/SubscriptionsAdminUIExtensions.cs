using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.AdminUI.DependencyInjection;

namespace MsgHatch.Subscriptions.AdminUI.DependencyInjection;

/// <summary>The setup method of the subscription admin UI (D239).</summary>
public static class SubscriptionsAdminUIExtensions
{
    /// <summary>
    /// Adds the subscription admin UI. The pages use <c>ISubscriptionQueries</c>, <c>ISubscriptionCommands</c>, and
    /// <c>IRoutingVerifier</c> of <c>TenantKey.None</c>, so the host selects the HTTP client or the in-process implementation in
    /// its setup (D235). The host adds the authorization, and maps the pages with the assembly of
    /// <see cref="SubscriptionAdminRoutes"/> as an additional assembly of its router. The method keeps the pages in a trimmed
    /// host. It adds the column "Subscriptions" of the message types page of the core admin UI (D276).
    /// </summary>
    /// <param name="services">The services of the host.</param>
    /// <returns>The same services.</returns>
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SubscriptionListPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SubscriptionEditPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RoutingVerificationPage))]
    public static IServiceCollection AddMsgHatchSubscriptionsAdminUI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddMessageTypeColumn<SubscriptionCountColumn>();
    }
}
