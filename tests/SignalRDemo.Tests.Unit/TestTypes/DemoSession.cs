using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using SignalRDemo.Notices;
using SignalRDemo.Sessions;

namespace SignalRDemo.Tests.Unit.TestTypes;

/// <summary>
/// One SignalR session of the demo on a <c>TestServer</c>, with a token of the demo login or anonymous, as a pane of the
/// dashboard. It sends its token in the query <c>access_token</c>, as the JavaScript client of a browser does.
/// </summary>
public sealed class DemoSession : IAsyncDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private readonly HubConnection _connection;
    private readonly ConcurrentQueue<DemoPayload> _received = new();
    private readonly TaskCompletionSource<JoinedSession> _joined = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private DemoSession(string tenant, string name, HubConnection connection)
    {
        Tenant = tenant;
        Name = name;
        _connection = connection;
        _connection.On<DemoPayload>(DemoNotice.TypeName, _received.Enqueue);
        _connection.On<JoinedSession>(DemoHub.JoinedMethod, session => _joined.TrySetResult(session));
    }

    public string Tenant { get; }

    /// <summary>The tenant and the user, or the tenant and <c>anonymous</c>, for example <c>eu anna</c>.</summary>
    public string Name { get; }

    public Task<JoinedSession> Joined => _joined.Task;

    public static async Task<DemoSession> SignInAsync(TestServer server, string tenant, string user, string? account, CancellationToken ct)
    {
        using var client = server.CreateClient();
        using var response = await client.PostAsJsonAsync(DemoLogin.Route, new LoginRequest(tenant, user, account), ct);
        var login = await response.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<LoginResponse>(ct);
        return await StartAsync(server, tenant, $"{tenant} {user}", login!.Token, joinTenant: false, ct);
    }

    public static Task<DemoSession> JoinTenantAsync(TestServer server, string tenant, CancellationToken ct) =>
        StartAsync(server, tenant, $"{tenant} anonymous", token: null, joinTenant: true, ct);

    public bool HasReceived(Guid id) => _received.Any(payload => payload.Id == id);

    public async Task WaitForAsync(Guid id, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(_timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        while (!HasReceived(id))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TimeProvider.System, linked.Token);
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static async Task<DemoSession> StartAsync(TestServer server, string tenant, string name, string? token, bool joinTenant, CancellationToken ct)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, DemoHub.Route), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = (context, cancel) =>
                    new ValueTask<WebSocket>(server.CreateWebSocketClient().ConnectAsync(WithQueryToken(context.Uri, token), cancel));
            })
            .Build();
        var session = new DemoSession(tenant, name, connection);
        await connection.StartAsync(ct);
        if (joinTenant)
        {
            await connection.InvokeAsync(DemoHub.JoinTenantMethod, tenant, ct);
        }

        await session.Joined.WaitAsync(_timeout, TimeProvider.System, ct);
        return session;
    }

    private static Uri WithQueryToken(Uri uri, string? token) =>
        token is null ? uri : new UriBuilder(uri) { Query = $"access_token={Uri.EscapeDataString(token)}" }.Uri;
}
