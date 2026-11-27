using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace MsgHatch.Tests.Shared;

/// <summary>
/// A TCP listener on the loopback address in place of a server. It takes each connection and closes it at once, so each
/// connect of a client fails in its handshake. It counts the connections. The unit tests of the dispatch signals link
/// this file.
/// </summary>
internal sealed class ClosingListener : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private int _connections;

    public ClosingListener()
    {
        _listener.Start();
        _ = CloseEachConnectionAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Connections => Volatile.Read(ref _connections);

    public void Dispose() => _listener.Dispose();

    private async Task CloseEachConnectionAsync()
    {
        try
        {
            while (true)
            {
                using var connection = await _listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref _connections);
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }
}

/// <summary>
/// A TCP listener on the loopback address in place of a server. It takes each connection, keeps it open, and never answers,
/// so a connect of a client waits in its handshake for the timeout of the client: 10 seconds for the RabbitMQ client, and
/// 15 seconds for Npgsql.
/// </summary>
internal sealed class SilentListener : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentBag<TcpClient> _connections = [];

    public SilentListener()
    {
        _listener.Start();
        _ = HoldEachConnectionAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Dispose()
    {
        _listener.Dispose();
        foreach (var connection in _connections)
        {
            connection.Dispose();
        }
    }

    private async Task HoldEachConnectionAsync()
    {
        try
        {
            while (true)
            {
                _connections.Add(await _listener.AcceptTcpClientAsync());
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }
}
