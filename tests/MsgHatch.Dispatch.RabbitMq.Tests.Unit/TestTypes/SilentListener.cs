using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace MsgHatch.Dispatch.RabbitMq.Tests.Unit.TestTypes;

/// <summary>
/// A TCP listener on the loopback address in place of a broker. It takes each connection, keeps it open, and never answers,
/// so a connect of the signal waits in the AMQP handshake for the handshake timeout of the client, 10 seconds.
/// </summary>
public sealed class SilentListener : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentBag<TcpClient> _connections = [];

    public SilentListener()
    {
        _listener.Start();
        _ = HoldEachConnectionAsync();
    }

    public Uri Uri => new($"amqp://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");

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
