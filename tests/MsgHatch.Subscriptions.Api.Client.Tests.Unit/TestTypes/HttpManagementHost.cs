using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Api.Client.DependencyInjection;
using MsgHatch.Subscriptions.AspNetCore;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Api.Client.Tests.Unit.TestTypes;

/// <summary>
/// The host of the HTTP implementation of a subscription management interface (D227): a web application on a
/// <see cref="TestServer"/> that maps the subscription endpoints on the services of a test, and a client host whose HTTP
/// clients use the handler of the <see cref="TestServer"/> as their primary handler. The server checks no setup at its
/// start, as the in-process host of the test base classes builds its services with no check.
/// </summary>
/// <typeparam name="T">The management interface.</typeparam>
public sealed class HttpManagementHost<T> : IManagementTestHost<T>
    where T : notnull
{
    private readonly WebApplication _server;
    private readonly AsyncServiceScope _serverScope;
    private readonly ServiceProvider _client;
    private readonly AsyncServiceScope _clientScope;

    internal HttpManagementHost(WebApplication server, ServiceProvider client)
    {
        _server = server;
        _serverScope = server.Services.CreateAsyncScope();
        _client = client;
        _clientScope = client.CreateAsyncScope();
    }

    public IServiceProvider Server => _serverScope.ServiceProvider;

    /// <summary>The services of the client host, in one DI scope.</summary>
    public IServiceProvider Client => _clientScope.ServiceProvider;

    public T Service(TenantKey key) => _clientScope.ServiceProvider.GetRequiredKeyedService<T>(key);

    public async ValueTask DisposeAsync()
    {
        await _clientScope.DisposeAsync();
        await _client.DisposeAsync();
        await _serverScope.DisposeAsync();
        await _server.StopAsync(TestContext.Current.CancellationToken);
        await _server.DisposeAsync();
    }
}

/// <summary>Starts the hosts of the HTTP implementation.</summary>
public static class HttpManagementHost
{
    public static readonly Uri BaseAddress = new("http://localhost/");

    /// <summary>
    /// Starts a server on the services of a test, and a client host with the same services and <c>UseSubscriptionApiClient</c>,
    /// so that the client host has the settings types and the trace type of the server.
    /// </summary>
    public static async ValueTask<IManagementTestHost<T>> StartAsync<T>(Action<IServiceCollection> addServices)
        where T : notnull =>
        await StartAsync<T>(addServices, (services, http) => AddSameServices(services, addServices).UseSubscriptionApiClient(BaseAddress, http));

    /// <summary>Starts a server on the services of a test, and a client host with its own services.</summary>
    /// <param name="addServerServices">Adds the services of the server with the store, the directory, and the registry.</param>
    /// <param name="addClientServices">Adds the services of the client host, and gives the change of each HTTP client to its setup method.</param>
    public static async ValueTask<HttpManagementHost<T>> StartAsync<T>(
        Action<IServiceCollection> addServerServices, Action<IServiceCollection, Action<IHttpClientBuilder>> addClientServices)
        where T : notnull
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        addServerServices(builder.Services);
        builder.Services.RemoveAll<IStartupValidator>();
        var server = builder.Build();
        server.MapMsgHatchSubscriptions();
        await server.StartAsync(TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        addClientServices(services, http => http.ConfigurePrimaryHttpMessageHandler(() => server.GetTestServer().CreateHandler()));
        return new HttpManagementHost<T>(server, services.Build());
    }

    /// <summary>Adds the services of a test to a client host, and gives its builder.</summary>
    public static MsgHatchBuilder AddSameServices(IServiceCollection services, Action<IServiceCollection> addServices)
    {
        addServices(services);
        return services.AddMsgHatch();
    }

    /// <summary>Adds the services of a client host with no role (D230), with the fake clock and the fake logging.</summary>
    public static MsgHatchBuilder AddClientHost(this IServiceCollection services) =>
        services.AddTestServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.None);
}
