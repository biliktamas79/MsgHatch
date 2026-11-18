using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Api.Client.DependencyInjection;
using MsgHatch.AspNetCore;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Api.Client.Tests.Unit.TestTypes;

/// <summary>
/// The host of the HTTP registry queries (D271): a web application on a <see cref="TestServer"/> that maps the registry
/// endpoints on the services of a test, and a client host with no role whose HTTP client uses the handler of the
/// <see cref="TestServer"/> as its primary handler. The server checks no setup at its start, as the in-process host builds
/// its services with no check.
/// </summary>
public sealed class HttpRegistryHost : IRegistryQueriesTestHost<IRegistryQueries>
{
    public static readonly Uri BaseAddress = new("http://localhost/");

    private readonly WebApplication _server;
    private readonly AsyncServiceScope _serverScope;
    private readonly ServiceProvider _client;
    private readonly AsyncServiceScope _clientScope;

    private HttpRegistryHost(WebApplication server, ServiceProvider client)
    {
        _server = server;
        _serverScope = server.Services.CreateAsyncScope();
        _client = client;
        _clientScope = client.CreateAsyncScope();
    }

    public IServiceProvider Server => _serverScope.ServiceProvider;

    /// <summary>The services of the client host, in one DI scope.</summary>
    public IServiceProvider Client => _clientScope.ServiceProvider;

    /// <summary>Starts a server on the services of a test, and a client host with no role and only <c>UseRegistryApiClient</c> (D230).</summary>
    public static async ValueTask<IRegistryQueriesTestHost<IRegistryQueries>> StartAsync(Action<IServiceCollection> addServerServices) =>
        await StartAsync(addServerServices, static (builder, http) => builder.UseRegistryApiClient(BaseAddress, http));

    /// <summary>Starts a server on the services of a test, and a client host whose setup a test gives.</summary>
    /// <param name="addServerServices">Adds the registry of the server.</param>
    /// <param name="selectClient">Selects the client on the builder of the client host, with the change of its HTTP client.</param>
    public static async ValueTask<HttpRegistryHost> StartAsync(
        Action<IServiceCollection> addServerServices, Func<MsgHatchBuilder, Action<IHttpClientBuilder>, MsgHatchBuilder> selectClient)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        addServerServices(builder.Services);
        builder.Services.RemoveAll<IStartupValidator>();
        var server = builder.Build();
        server.MapMsgHatchRegistry();
        await server.StartAsync(TestContext.Current.CancellationToken);
        var services = TestHost.CreateServices();
        selectClient(
            services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.None),
            http => http.ConfigurePrimaryHttpMessageHandler(() => server.GetTestServer().CreateHandler()));
        return new HttpRegistryHost(server, services.Build());
    }

    public IRegistryQueries Service(TenantKey key) => _clientScope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(key);

    public async ValueTask DisposeAsync()
    {
        await _clientScope.DisposeAsync();
        await _client.DisposeAsync();
        await _serverScope.DisposeAsync();
        await _server.StopAsync(TestContext.Current.CancellationToken);
        await _server.DisposeAsync();
    }
}
