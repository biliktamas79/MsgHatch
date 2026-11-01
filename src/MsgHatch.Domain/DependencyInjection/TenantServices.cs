using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// Registers a tenant service (D126): a keyed service for each <see cref="TenantKey"/> through
/// <see cref="KeyedService.AnyKey"/>, and an unkeyed registration that gives the service of <see cref="TenantKey.None"/>.
/// </summary>
internal static class TenantServices
{
    public static void Add<TService>(
        IServiceCollection services,
        MsgHatchSetup setup,
        ServiceLifetime lifetime,
        Func<IServiceProvider, TenantKey, TService?> create,
        Func<IServiceProvider, TenantKey, TService, TService>? wrapWithLogging = null)
        where TService : class
    {
        setup.TenantServiceTypes.Add(typeof(TService));
        services.Add(new ServiceDescriptor(
            typeof(TService),
            KeyedService.AnyKey,
            (provider, key) => Create(provider, TenantKeyOf(key), setup, create, wrapWithLogging)!,
            lifetime));
        services.Add(new ServiceDescriptor(
            typeof(TService),
            static provider => provider.GetKeyedService<TService>(TenantKey.None)!,
            lifetime));
    }

    private static TService? Create<TService>(
        IServiceProvider provider,
        TenantKey key,
        MsgHatchSetup setup,
        Func<IServiceProvider, TenantKey, TService?> create,
        Func<IServiceProvider, TenantKey, TService, TService>? wrapWithLogging)
        where TService : class
    {
        var tenantServices = new TenantServiceProvider(provider, key, setup.TenantServiceTypes);
        var service = create(tenantServices, key);
        return service is null || wrapWithLogging is null || !Loggers.IsEnabled(provider)
            ? service
            : wrapWithLogging(tenantServices, key, service);
    }

    private static TenantKey TenantKeyOf(object? key) =>
        key as TenantKey?
        ?? throw new InvalidOperationException("A tenant service resolves only with a TenantKey as its key.");
}
