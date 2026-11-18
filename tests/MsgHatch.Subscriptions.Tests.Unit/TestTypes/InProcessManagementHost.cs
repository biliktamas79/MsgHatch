using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>The host of the in-process implementation of a management interface: one DI container and one scope.</summary>
/// <typeparam name="T">The management interface.</typeparam>
public sealed class InProcessManagementHost<T> : IManagementTestHost<T>
    where T : notnull
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    public InProcessManagementHost(Action<IServiceCollection> addServices)
    {
        ArgumentNullException.ThrowIfNull(addServices);
        var services = new ServiceCollection();
        addServices(services);
        _provider = services.Build();
        _scope = _provider.CreateAsyncScope();
    }

    public IServiceProvider Server => _scope.ServiceProvider;

    public T Service(TenantKey key) => _scope.ServiceProvider.GetRequiredKeyedService<T>(key);

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }
}

/// <summary>Starts the host of an in-process implementation.</summary>
public static class InProcessManagementHost
{
    public static ValueTask<IManagementTestHost<T>> StartAsync<T>(Action<IServiceCollection> addServices)
        where T : notnull =>
        ValueTask.FromResult<IManagementTestHost<T>>(new InProcessManagementHost<T>(addServices));
}
