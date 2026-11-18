using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions;

/// <summary>The factory of the subscription router of each event type, which <c>UseSubscriptions</c> selects (D99, D237).</summary>
internal sealed class SubscriptionRouterFactory : IOpenRouterFactory
{
    public static SubscriptionRouterFactory Instance { get; } = new();

    public IMessageRouter<TEvent> Create<TEvent>(IServiceProvider tenantServices, TenantKey key) =>
        ActivatorUtilities.CreateInstance<SubscriptionRouter<TEvent>>(tenantServices);
}
