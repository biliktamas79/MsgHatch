using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// Makes the router of each message type with no router of its own, for example the subscription router (D99). One factory
/// object makes the router of each event type with a generic method, so no code closes an open generic type at run time
/// (D237).
/// </summary>
public interface IOpenRouterFactory
{
    /// <summary>Makes the router of one event type and tenant key.</summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <param name="tenantServices">The service provider of the tenant services of the key.</param>
    /// <param name="key">The tenant key.</param>
    /// <returns>The router.</returns>
    /// <exception cref="InvalidOperationException">The factory cannot make a router for the event type.</exception>
    IMessageRouter<TEvent> Create<TEvent>(IServiceProvider tenantServices, TenantKey key);
}
