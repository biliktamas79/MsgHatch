using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// The service provider of one tenant key (D126). It gives the key itself, each tenant service of the same key, and
/// each other service as the inner provider gives it.
/// </summary>
internal sealed class TenantServiceProvider(IServiceProvider services, TenantKey key, IReadOnlySet<Type> tenantServiceTypes)
    : IServiceProvider, IServiceProviderIsService
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(TenantKey))
        {
            return key;
        }

        if (serviceType == typeof(IServiceProviderIsService))
        {
            return this;
        }

        return tenantServiceTypes.Contains(serviceType) ? KeyedServices().GetKeyedService(serviceType, key) : services.GetService(serviceType);
    }

    public bool IsService(Type serviceType) =>
        serviceType == typeof(TenantKey) || (services.GetService<IServiceProviderIsService>()?.IsService(serviceType) ?? false);

    private IKeyedServiceProvider KeyedServices() =>
        services as IKeyedServiceProvider
        ?? throw new InvalidOperationException("A tenant service needs a service provider with keyed services.");
}
