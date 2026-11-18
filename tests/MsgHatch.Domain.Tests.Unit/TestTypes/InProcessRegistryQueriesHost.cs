using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>The host of the in-process registry queries: one DI container of <see cref="TestHost"/> and one scope.</summary>
/// <typeparam name="T">The query interface.</typeparam>
public sealed class InProcessRegistryQueriesHost<T> : IRegistryQueriesTestHost<T>
    where T : notnull
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    public InProcessRegistryQueriesHost(Action<IServiceCollection> addServices)
    {
        ArgumentNullException.ThrowIfNull(addServices);
        var services = TestHost.CreateServices();
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

/// <summary>Starts the host of the in-process registry queries.</summary>
public static class InProcessRegistryQueriesHost
{
    public static ValueTask<IRegistryQueriesTestHost<T>> StartAsync<T>(Action<IServiceCollection> addServices)
        where T : notnull =>
        ValueTask.FromResult<IRegistryQueriesTestHost<T>>(new InProcessRegistryQueriesHost<T>(addServices));
}
